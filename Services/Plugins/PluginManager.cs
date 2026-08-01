using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using MarkdownVault.PluginSdk;
using MarkdownVault.Services;

namespace MarkdownVault.Services.Plugins;

/// <summary>
/// Descubre plugins bajo la carpeta <c>Plugins/</c> (junto al ejecutable), carga
/// los que corresponda y les deja registrar contribuciones en el
/// <see cref="PluginRegistry"/> compartido. Aísla cada plugin: si uno falla,
/// queda marcado como <see cref="PluginState.Failed"/> y la app sigue.
/// </summary>
public sealed class PluginManager
{
    private readonly PluginRegistry  _registry;
    private readonly IHostServices   _host;
    private readonly SettingsService _settings;
    private readonly string          _pluginsRoot;
    private readonly string          _pluginDataRoot;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public PluginManager(
        PluginRegistry registry, IHostServices host, SettingsService settings,
        string? pluginsRoot = null, string? pluginDataRoot = null)
    {
        _registry       = registry;
        _host           = host;
        _settings       = settings;
        _pluginsRoot    = pluginsRoot ?? Path.Combine(AppContext.BaseDirectory, "Plugins");
        _pluginDataRoot = pluginDataRoot ?? Path.Combine(AppPaths.Root, "PluginData");
    }

    private readonly List<PluginDescriptor> _plugins = new();

    // Contextos de carga de los plugins ACTUALMENTE activos (para poder descargarlos).
    private readonly Dictionary<string, (PluginLoadContext Alc, IPlugin Plugin)> _loaded =
        new(StringComparer.OrdinalIgnoreCase);

    // IPluginContext entregado a cada plugin activo en Configure (sólo para tests:
    // permite inspeccionar el Storage sandboxeado que recibió cada uno).
    private readonly Dictionary<string, IPluginContext> _activeContexts =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Plugins descubiertos con su estado (para la sección de UI).</summary>
    public IReadOnlyList<PluginDescriptor> Plugins => _plugins;

    /// <summary>Ruta de la carpeta escaneada (útil para diagnóstico en la UI).</summary>
    public string PluginsRoot => _pluginsRoot;

    /// <summary>
    /// Escanea la carpeta, calcula el estado habilitado desde settings (default =
    /// habilitado) y CARGA sólo los plugins habilitados (los deshabilitados no
    /// ejecutan código). Notifica a los consumidores UNA vez al final.
    /// </summary>
    public void LoadAll()
    {
        UnloadAll();
        _plugins.Clear();
        _registry.Clear();

        foreach (var descriptor in Discover())
            _plugins.Add(descriptor);

        var saved = _settings.Load().PluginsEnabled;
        foreach (var d in _plugins)
            d.Enabled = d.State != PluginState.Failed
                        && (!saved.TryGetValue(d.Metadata.Id, out var on) || on);

        // Sólo se carga (DLL en memoria + Configure) lo habilitado.
        foreach (var d in _plugins.Where(p => p.Enabled).ToList())
            if (!Activate(d))
                d.Enabled = false;   // Activate marcó Failed

        _registry.SetEnabledSet(_plugins.Where(p => p.Enabled).Select(p => p.Metadata.Id));
        _registry.RaiseChanged();
    }

    /// <summary>
    /// Habilita/deshabilita en caliente. Habilitar CARGA el DLL (Configure);
    /// deshabilitar quita las contribuciones y DESCARGA el DLL (Unload + GC).
    /// Persiste en settings y notifica (el preview se re-renderiza).
    /// </summary>
    public void SetEnabled(string pluginId, bool enabled)
    {
        var d = _plugins.FirstOrDefault(p => p.Metadata.Id == pluginId);
        if (d is null || d.State == PluginState.Failed || d.Enabled == enabled)
            return;

        if (enabled)
        {
            if (Activate(d))
            {
                d.Enabled = true;
                _registry.SetEnabled(pluginId, true);
                _registry.RaiseChanged();
            }
            else
            {
                d.Enabled = false;   // Activate falló → quedó Failed
            }
        }
        else
        {
            d.Enabled = false;
            Deactivate(d);   // RemoveByOwner + RaiseChanged + Unload + GC
        }

        var settings = _settings.Load();
        settings.PluginsEnabled[pluginId] = d.Enabled;
        _settings.Save(settings);
    }

    // ─── Descubrimiento ──────────────────────────────────────────────────────

    private IEnumerable<PluginDescriptor> Discover()
    {
        if (!Directory.Exists(_pluginsRoot))
            yield break;

        foreach (var dir in Directory.GetDirectories(_pluginsRoot))
        {
            var manifestPath = Path.Combine(dir, "plugin.json");
            if (!File.Exists(manifestPath))
                continue;

            PluginManifest? manifest = null;
            string?         parseError = null;
            try
            {
                manifest = JsonSerializer.Deserialize<PluginManifest>(
                    File.ReadAllText(manifestPath), JsonOpts);
            }
            catch (Exception ex)
            {
                parseError = $"plugin.json inválido: {ex.Message}";
            }

            if (manifest is null)
            {
                yield return Failed(dir, parseError ?? "plugin.json vacío.");
                continue;
            }
            if (!manifest.IsValid(out var validationError))
            {
                yield return Failed(dir, validationError!, manifest);
                continue;
            }
            if (!IsSdkCompatible(manifest.MinSdk))
            {
                yield return Failed(dir,
                    $"Requiere SDK {manifest.MinSdk}; el host provee {SdkInfo.Version}.", manifest);
                continue;
            }

            var entry = Path.Combine(dir, manifest.Entry);
            yield return new PluginDescriptor
            {
                Metadata     = ToMetadata(manifest),
                FolderPath   = dir,
                EntryDllPath = entry,
                State        = File.Exists(entry) ? PluginState.Discovered : PluginState.Failed,
                Error        = File.Exists(entry) ? null : $"No se encontró el DLL de entrada: {manifest.Entry}"
            };
        }
    }

    // ─── Activación / desactivación ──────────────────────────────────────────

    /// <summary>Carga el DLL en un contexto collectible y ejecuta Configure. Devuelve false y marca Failed si algo explota.</summary>
    private bool Activate(PluginDescriptor descriptor)
    {
        var id = descriptor.Metadata.Id;
        if (descriptor.State == PluginState.Failed) return false;
        if (_loaded.ContainsKey(id)) return true;

        try
        {
            var alc = new PluginLoadContext(descriptor.EntryDllPath);
            var asm = alc.LoadFromAssemblyPath(descriptor.EntryDllPath);

            var pluginType = asm.GetTypes().FirstOrDefault(t =>
                typeof(IPlugin).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false });

            if (pluginType is null)
                throw new InvalidOperationException("El ensamblado no contiene una clase IPlugin.");

            var plugin  = (IPlugin)Activator.CreateInstance(pluginType)!;
            var storage = new PluginStorage(Path.Combine(_pluginDataRoot, id));
            var ctx     = new HostPluginContext(descriptor.Metadata, _host, _registry, descriptor.FolderPath, storage);
            plugin.Configure(ctx);

            if (plugin is IActivatablePlugin activatable)
                activatable.OnActivatedAsync().GetAwaiter().GetResult();

            _loaded[id]         = (alc, plugin);
            _activeContexts[id] = ctx;
            descriptor.State    = PluginState.Active;
            descriptor.Error    = null;
            return true;
        }
        catch (Exception ex)
        {
            descriptor.State = PluginState.Failed;
            descriptor.Error = ex.Message;
            System.Diagnostics.Debug.WriteLine($"[PluginManager] {id} falló: {ex}");
            return false;
        }
    }

    /// <summary>Quita las contribuciones del plugin y descarga su DLL (Unload + GC).</summary>
    private void Deactivate(PluginDescriptor descriptor)
    {
        var id = descriptor.Metadata.Id;
        if (!_loaded.TryGetValue(id, out var loaded))
            return;

        if (loaded.Plugin is IActivatablePlugin activatable)
        {
            try { activatable.OnDeactivatedAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PluginManager] OnDeactivated {id}: {ex}"); }
        }

        _loaded.Remove(id);
        _activeContexts.Remove(id);
        _registry.RemoveByOwner(id);
        descriptor.State = PluginState.Discovered;

        // Notificar ANTES de descargar: invalida el pipeline cacheado y reconstruye
        // la toolbar, soltando las referencias a tipos del plugin. Recién luego, unload.
        _registry.RaiseChanged();

        loaded.Alc.Unload();
        loaded = default;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private void UnloadAll()
    {
        foreach (var loaded in _loaded.Values)
            loaded.Alc.Unload();
        _loaded.Clear();
        _activeContexts.Clear();
    }

    /// <summary>
    /// Sólo para tests: el <see cref="IPluginContext"/> que recibió Configure() para un
    /// plugin ACTUALMENTE activo (permite inspeccionar su Storage sandboxeado). Null si
    /// el plugin no está activo.
    /// </summary>
    internal IPluginContext? GetActiveContextForTest(string pluginId) =>
        _activeContexts.TryGetValue(pluginId, out var ctx) ? ctx : null;

    /// <summary>
    /// Sólo para tests: carga y descarga un DLL en un contexto collectible y devuelve
    /// una referencia débil al contexto, para verificar que se libera tras GC.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static WeakReference LoadUnloadForTest(string pluginDllPath)
    {
        var alc = new PluginLoadContext(pluginDllPath);
        alc.LoadFromAssemblyPath(pluginDllPath);
        var weak = new WeakReference(alc);
        alc.Unload();
        return weak;
    }

    /// <summary>
    /// Sólo para tests: como <see cref="LoadUnloadForTest"/>, pero además ejecuta el
    /// trabajo del plugin de fixture (<c>MarkdownVault.Plugin.TestFixture.WpfFixturePlugin.DoWork</c>
    /// — abre/cierra una <c>Window</c> WPF real del tipo BASE y adquiere/libera un
    /// <c>FileStream</c>) en un hilo STA DEDICADO antes de descargar, porque el hilo de
    /// xUnit corre en MTA y <c>Window</c> exige STA. Usado por el test de
    /// caracterización que documenta la limitación ACEPTADA para v1 (ver
    /// docs/plugins/GUIA-PLUGINS.md §9): en AISLAMIENTO este unload es limpio (el tipo
    /// base <c>Window</c> no fija nada), pero DESPUÉS de activar un plugin real que
    /// define su propio tipo <c>Window</c>-derivado (Eisenhower/<c>CaptureModal</c>,
    /// vía <see cref="Activate"/>'s <c>asm.GetTypes()</c>), este mismo unload — de un
    /// ALC totalmente distinto — también queda clavado, porque WPF corrompe sus
    /// cachés estáticas de PROCESO, no sólo las del tipo/ALC que las disparó. Invoca
    /// por reflexión: el código de este método nunca toma una referencia de IL a un
    /// tipo del ensamblado del fixture (igual que <see cref="Activate"/> sólo castea al
    /// <c>IPlugin</c> compartido) — una referencia directa forzaría la carga del
    /// ensamblado del fixture en el ALC por defecto y arruinaría la prueba de
    /// descarga. Devuelve una referencia débil al ALC.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static WeakReference LoadUnloadWithWorkForTest(string pluginDllPath)
    {
        var alc = new PluginLoadContext(pluginDllPath);
        var asm = alc.LoadFromAssemblyPath(pluginDllPath);

        var type = asm.GetType("MarkdownVault.Plugin.TestFixture.WpfFixturePlugin")
                   ?? throw new InvalidOperationException(
                       "No se encontró MarkdownVault.Plugin.TestFixture.WpfFixturePlugin en el fixture.");
        var method = type.GetMethod("DoWork", BindingFlags.Public | BindingFlags.Static)
                     ?? throw new InvalidOperationException("El fixture no expone un método estático DoWork().");

        Exception? staException = null;
        var sta = new Thread(() =>
        {
            try { method.Invoke(null, null); }
            catch (Exception ex) { staException = ex; }
        });
        sta.SetApartmentState(ApartmentState.STA);
        sta.IsBackground = true;
        sta.Start();
        sta.Join();

        // Soltar toda referencia a tipos del ensamblado del fixture ANTES de descargar,
        // para que sólo quede la del ALC (vía `weak`) — determinismo del test de GC.
        asm    = null!;
        type   = null!;
        method = null!;
        sta    = null!;

        if (staException is not null)
            throw new InvalidOperationException("El trabajo del fixture falló en el hilo STA.", staException);

        var weak = new WeakReference(alc);
        alc.Unload();
        return weak;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static bool IsSdkCompatible(string minSdk)
        => Version.TryParse(minSdk, out var min)
           && Version.TryParse(SdkInfo.Version, out var cur)
           && min <= cur;

    private static PluginMetadata ToMetadata(PluginManifest m) => new()
    {
        Id          = m.Id,
        Name        = m.Name,
        Version     = m.Version,
        Description = m.Description,
        Author      = m.Author,
        MinSdk      = m.MinSdk
    };

    private static PluginDescriptor Failed(string dir, string error, PluginManifest? m = null) => new()
    {
        Metadata     = m is null
            ? new PluginMetadata { Id = Path.GetFileName(dir), Name = Path.GetFileName(dir) }
            : ToMetadata(m),
        FolderPath   = dir,
        EntryDllPath = m is null ? "" : Path.Combine(dir, m.Entry),
        State        = PluginState.Failed,
        Error        = error
    };
}
