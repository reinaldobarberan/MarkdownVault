using System.IO;
using MarkdownVault.Services;
using MarkdownVault.Services.Plugins;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Integración: carga los plugins REALES ya compilados (bin/.../Plugins) a través de
/// <see cref="PluginManager"/> + AssemblyLoadContext y verifica activación,
/// contribuciones y render. Cierra el hueco que los tests unitarios no cubren
/// (la activación exitosa de un DLL real).
/// </summary>
public class PluginActivationIntegrationTests : IDisposable
{
    private readonly string _settingsPath =
        Path.Combine(Path.GetTempPath(), $"mvint_{Guid.NewGuid():N}.json");

    /// <summary>Ubica bin/&lt;Config&gt;/net8.0-windows/Plugins subiendo hasta el .sln.</summary>
    private static string? FindHostPluginsRoot()
    {
        var baseDir = AppContext.BaseDirectory;
        var dir     = new DirectoryInfo(baseDir);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MarkdownVault.sln")))
            dir = dir.Parent;
        if (dir is null) return null;

        var config = baseDir.Replace('\\', '/').Contains("/Release/") ? "Release" : "Debug";
        return Path.Combine(dir.FullName, "bin", config, "net8.0-windows", "Plugins");
    }

    private PluginManager NewManager(PluginRegistry registry, string pluginsRoot) =>
        new(registry, new FakeHost(), new SettingsService(_settingsPath), pluginsRoot);

    [Fact]
    public void Real_plugins_load_activate_and_contribute()
    {
        var pluginsRoot = FindHostPluginsRoot();
        // En `dotnet test` el host se compila antes, así que la carpeta existe.
        // Guarda defensiva: si no está, no hay nada que integrar.
        if (pluginsRoot is null || !Directory.Exists(pluginsRoot))
            return;

        var registry = new PluginRegistry();
        var manager  = NewManager(registry, pluginsRoot);
        manager.LoadAll();

        var mermaidPlugin = manager.Plugins.FirstOrDefault(p => p.Metadata.Id == "core.mermaid");
        Assert.NotNull(mermaidPlugin);
        Assert.Equal(PluginState.Active, mermaidPlugin!.State);
        Assert.True(mermaidPlugin.Enabled);

        // Contribuciones vivas (habilitado por defecto). Uso "mermaid@11" (la URL de la
        // librería) para no matchear el ":not(.language-mermaid)" del plugin Highlight.
        Assert.Contains(registry.PreviewAssets, a => a.Value.Contains("mermaid@11"));
        Assert.Contains(registry.CommandGroups, g => g.Title == "Mermaid");

        // Render real: el script de Mermaid queda inyectado en la página.
        var html = new MarkdownService(registry).RenderToHtml("# hola", isDarkTheme: false);
        Assert.Contains("mermaid@11", html);
    }

    [Fact]
    public void Disabling_a_real_plugin_hides_its_contributions()
    {
        var pluginsRoot = FindHostPluginsRoot();
        // En `dotnet test` el host se compila antes, así que la carpeta existe.
        // Guarda defensiva: si no está, no hay nada que integrar.
        if (pluginsRoot is null || !Directory.Exists(pluginsRoot))
            return;

        var registry = new PluginRegistry();
        var manager  = NewManager(registry, pluginsRoot);
        manager.LoadAll();
        Assert.Contains(registry.CommandGroups, g => g.Title == "Mermaid");

        manager.SetEnabled("core.mermaid", false);

        Assert.DoesNotContain(registry.CommandGroups, g => g.Title == "Mermaid");
        Assert.DoesNotContain(registry.PreviewAssets, a => a.Value.Contains("mermaid@11"));
    }

    [Fact]
    public void Disable_then_reenable_restores_contributions()
    {
        var pluginsRoot = FindHostPluginsRoot();
        if (pluginsRoot is null || !Directory.Exists(pluginsRoot))
            return;

        var registry = new PluginRegistry();
        var manager  = NewManager(registry, pluginsRoot);
        manager.LoadAll();

        manager.SetEnabled("core.mermaid", false);   // descarga el DLL
        Assert.DoesNotContain(registry.CommandGroups, g => g.Title == "Mermaid");

        manager.SetEnabled("core.mermaid", true);    // recarga el DLL
        Assert.Contains(registry.CommandGroups, g => g.Title == "Mermaid");
        Assert.Contains(registry.PreviewAssets, a => a.Value.Contains("mermaid@11"));
    }

    [Fact]
    public void Collectible_context_is_unloaded_after_gc()
    {
        var pluginsRoot = FindHostPluginsRoot();
        var mermaidDll  = pluginsRoot is null
            ? null
            : Path.Combine(pluginsRoot, "Mermaid", "MarkdownVault.Plugin.Mermaid.dll");
        if (mermaidDll is null || !File.Exists(mermaidDll))
            return;

        // Carga+descarga aislada; el ALC debe liberarse cuando ya no hay referencias.
        var weak = PluginManager.LoadUnloadForTest(mermaidDll);
        for (var i = 0; i < 10 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(weak.IsAlive, "El AssemblyLoadContext no se liberó — quedó una referencia viva.");
    }

    /// <summary>
    /// CARACTERIZACIÓN de una limitación ACEPTADA para v1 (decisión del usuario — ver
    /// engram topic_key <c>architecture/wpf-alc-pin</c> y
    /// <c>docs/plugins/GUIA-PLUGINS.md</c> §9 "Plugins con UI WPF y descarga en
    /// caliente"): una vez que CUALQUIER plugin real que DEFINE su propio tipo
    /// derivado de <see cref="System.Windows.Window"/> (ej. el <c>CaptureModal</c> del
    /// plugin Eisenhower) se activa — <see cref="PluginManager.Activate"/> lo ubica vía
    /// <c>asm.GetTypes()</c>, igual que <see cref="Real_plugins_load_activate_and_contribute"/>
    /// — WPF registra ese tipo en cachés estáticas de PROCESO no-evictables. A partir
    /// de ahí, NINGÚN <c>AssemblyLoadContext</c> collectible que toque WPF (ni siquiera
    /// uno que sólo use el tipo BASE <c>Window</c>, como el fixture de este test) vuelve
    /// a liberarse por el resto de la vida del proceso: no es un pin acotado al
    /// tipo/ALC que lo disparó, es una corrupción de las cachés estáticas de WPF a
    /// nivel proceso.
    ///
    /// Este test reproduce la secuencia EXACTA confirmada empíricamente (ver engram
    /// <c>sdd/eisenhower-plugin/wpf-alc-pin-gotcha</c>: "sólo `Real_plugins_load_activate_and_contribute`
    /// + el test de unload WPF juntos, 100%") de forma AUTOCONTENIDA en un único test
    /// — sin depender del orden de otros tests de la clase: primero activa los plugins
    /// reales (incluye Eisenhower), LUEGO intenta descargar el ALC del fixture WPF, y
    /// afirma que NO se libera.
    ///
    /// Por eso este test NO afirma que el ALC se libera — afirma la realidad aceptada:
    /// NO se libera. El fix arquitectónico correcto (que ningún plugin defina tipos
    /// WPF — el host media un diálogo vía el SDK) queda DIFERIDO a SDK v1.2; no se
    /// construye acá. Si esta afirmación deja de reproducirse de forma estable en el
    /// futuro (ej. cambios de runtime .NET/WPF), reemplazar por
    /// <c>[Fact(Skip="motivo completo")]</c> en vez de borrar el test.
    /// </summary>
    [Fact]
    public void Wpf_window_plugin_pins_its_ALC_documented_v1_limitation()
    {
        var pluginsRoot = FindHostPluginsRoot();
        if (pluginsRoot is null || !Directory.Exists(pluginsRoot))
            return; // guarda defensiva, igual que el resto de esta clase.

        // Paso 1: activar los plugins REALES — incluye Eisenhower, que define su
        // propio tipo Window-derivado (CaptureModal). PluginManager.Activate lo ubica
        // vía asm.GetTypes(), forzando a WPF a registrarlo en sus cachés estáticas.
        var registry = new PluginRegistry();
        var manager  = NewManager(registry, pluginsRoot);
        manager.LoadAll();
        var eisenhower = manager.Plugins.FirstOrDefault(p => p.Metadata.Id == "core.eisenhower");
        Assert.NotNull(eisenhower);
        Assert.Equal(PluginState.Active, eisenhower!.State);

        // Paso 2: intentar descargar un ALC collectible TOTALMENTE DISTINTO — el
        // fixture WPF, que sólo usa el tipo BASE System.Windows.Window (nunca falla en
        // aislamiento, ver WpfFixturePlugin.DoWork). Después del paso 1, sí falla.
        var fixtureDll = Path.Combine(AppContext.BaseDirectory, "MarkdownVault.Plugin.TestFixture.dll");
        Assert.True(File.Exists(fixtureDll),
            $"No se encontró el fixture — revisar el ProjectReference en MarkdownVault.Tests.csproj: {fixtureDll}");

        var weak = PluginManager.LoadUnloadWithWorkForTest(fixtureDll);
        for (var i = 0; i < 10 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.True(weak.IsAlive,
            "El AssemblyLoadContext se liberó — si esto empieza a pasar, la limitación " +
            "documentada en GUIA-PLUGINS.md §9 ya no aplica (revisar runtime .NET/WPF); " +
            "este test y esa doc deben actualizarse juntos, no basta con borrar el test.");
    }

    [Fact]
    public void Activated_real_plugins_get_isolated_storage_under_their_own_id()
    {
        var pluginsRoot = FindHostPluginsRoot();
        if (pluginsRoot is null || !Directory.Exists(pluginsRoot))
            return;

        var dataRoot = Path.Combine(Path.GetTempPath(), $"mvdata_{Guid.NewGuid():N}");
        try
        {
            var registry = new PluginRegistry();
            var manager  = new PluginManager(
                registry, new FakeHost(), new SettingsService(_settingsPath), pluginsRoot, dataRoot);
            manager.LoadAll();

            var mermaidCtx   = manager.GetActiveContextForTest("core.mermaid");
            var highlightCtx = manager.GetActiveContextForTest("core.highlight");

            Assert.NotNull(mermaidCtx);
            Assert.NotNull(highlightCtx);

            Assert.Equal(
                Path.GetFullPath(Path.Combine(dataRoot, "core.mermaid")),
                Path.GetFullPath(mermaidCtx!.Storage.RootPath));
            Assert.Equal(
                Path.GetFullPath(Path.Combine(dataRoot, "core.highlight")),
                Path.GetFullPath(highlightCtx!.Storage.RootPath));

            Assert.NotEqual(mermaidCtx.Storage.RootPath, highlightCtx.Storage.RootPath);
        }
        finally
        {
            try { Directory.Delete(dataRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    public void Dispose()
    {
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch { /* best effort */ }
    }
}
