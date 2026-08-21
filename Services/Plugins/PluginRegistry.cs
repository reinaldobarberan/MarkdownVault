using MarkdownVault.PluginSdk;

namespace MarkdownVault.Services.Plugins;

/// <summary>
/// Agregador central de contribuciones. Es la ÚNICA fuente de verdad que
/// consultan <see cref="MarkdownService"/> (assets) y —a futuro— el editor
/// (comandos, paneles).
///
/// Cada contribución se etiqueta con el id del plugin dueño. Las consultas
/// devuelven SOLO las de plugins habilitados (<see cref="SetEnabled"/>), de modo
/// que activar/desactivar es instantáneo: se cambia el set y se emite
/// <see cref="Changed"/> (el DLL sigue cargado; v1 no descarga ensamblados).
/// </summary>
public sealed class PluginRegistry
{
    private readonly List<(string Owner, PreviewAsset Asset)>              _previewAssets = new();
    private readonly List<(string Owner, PluginCommand Cmd)>              _commands      = new();
    private readonly List<(string Owner, PluginCommandGroup Group)>       _commandGroups = new();
    private readonly List<(string Owner, int Order, IMarkdownContribution C)> _markdown  = new();
    private readonly List<(string Owner, PluginPanel Panel)>              _panels        = new();
    private readonly List<(string Owner, PluginListSetting Setting)>      _listSettings  = new();
    private readonly List<(string Owner, Action<VaultEvent> Handler)>     _vaultHandlers = new();

    private readonly HashSet<string> _enabled = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Se dispara cuando cambia el conjunto activo de contribuciones.</summary>
    public event Action? Changed;

    // ─── Consultas (filtradas por plugins habilitados) ───────────────────────

    public IReadOnlyList<PreviewAsset> PreviewAssets =>
        _previewAssets.Where(x => _enabled.Contains(x.Owner)).Select(x => x.Asset).ToList();

    public IReadOnlyList<PluginCommand> Commands =>
        _commands.Where(x => _enabled.Contains(x.Owner)).Select(x => x.Cmd).ToList();

    public IReadOnlyList<PluginCommandGroup> CommandGroups =>
        _commandGroups.Where(x => _enabled.Contains(x.Owner)).Select(x => x.Group).ToList();

    public IReadOnlyList<PluginPanel> Panels =>
        _panels.Where(x => _enabled.Contains(x.Owner)).Select(x => x.Panel).ToList();

    public IReadOnlyList<PluginListSetting> ListSettings =>
        _listSettings.Where(x => _enabled.Contains(x.Owner)).Select(x => x.Setting).ToList();

    /// <summary>
    /// Las listas editables de UN plugin. La ventana de complementos las dibuja
    /// bajo la fila de su dueño, así que necesita preguntarlas por dueño y no en
    /// bloque. Un plugin deshabilitado devuelve vacío, igual que el resto de las
    /// consultas.
    /// </summary>
    public IReadOnlyList<PluginListSetting> ListSettingsFor(string owner) =>
        _listSettings.Where(x => Same(x.Owner, owner) && _enabled.Contains(x.Owner))
                     .Select(x => x.Setting).ToList();

    public IEnumerable<IMarkdownContribution> MarkdownContributions =>
        _markdown.Where(x => _enabled.Contains(x.Owner)).OrderBy(x => x.Order).Select(x => x.C);

    public IReadOnlyList<Action<VaultEvent>> VaultHandlers =>
        _vaultHandlers.Where(x => _enabled.Contains(x.Owner)).Select(x => x.Handler).ToList();

    // ─── Registro (lo usa HostPluginContext, con el id del dueño) ─────────────

    public void AddPreviewAsset(string owner, PreviewAsset asset)            => _previewAssets.Add((owner, asset));
    public void AddCommand(string owner, PluginCommand command)             => _commands.Add((owner, command));
    public void AddCommandGroup(string owner, PluginCommandGroup group)     => _commandGroups.Add((owner, group));
    public void AddMarkdownContribution(string owner, IMarkdownContribution c, int order) => _markdown.Add((owner, order, c));
    public void AddPanel(string owner, PluginPanel panel)                   => _panels.Add((owner, panel));
    public void AddListSetting(string owner, PluginListSetting setting)     => _listSettings.Add((owner, setting));
    public void AddVaultHandler(string owner, Action<VaultEvent> handler)   => _vaultHandlers.Add((owner, handler));

    // ─── Estado habilitado/deshabilitado ─────────────────────────────────────

    public bool IsEnabled(string pluginId) => _enabled.Contains(pluginId);

    /// <summary>Reemplaza el conjunto de plugins habilitados (usado al cargar).</summary>
    public void SetEnabledSet(IEnumerable<string> pluginIds)
    {
        _enabled.Clear();
        foreach (var id in pluginIds) _enabled.Add(id);
    }

    /// <summary>Habilita o deshabilita un plugin puntual.</summary>
    public void SetEnabled(string pluginId, bool enabled)
    {
        if (enabled) _enabled.Add(pluginId);
        else         _enabled.Remove(pluginId);
    }

    /// <summary>
    /// Elimina TODAS las contribuciones de un plugin (para descargar su DLL). A
    /// diferencia del gating, esto suelta las referencias a tipos del plugin
    /// (delegates, extensiones Markdig), condición necesaria para el unload.
    /// </summary>
    public void RemoveByOwner(string owner)
    {
        _previewAssets.RemoveAll(x => Same(x.Owner, owner));
        _commands.RemoveAll(x => Same(x.Owner, owner));
        _commandGroups.RemoveAll(x => Same(x.Owner, owner));
        _markdown.RemoveAll(x => Same(x.Owner, owner));
        _panels.RemoveAll(x => Same(x.Owner, owner));
        // Los Load/Save/Describe de una lista son delegates que apuntan a código del
        // plugin: si sobrevivieran acá, el ALC no se descargaría. Misma razón que los
        // comandos, ni más ni menos.
        _listSettings.RemoveAll(x => Same(x.Owner, owner));
        _vaultHandlers.RemoveAll(x => Same(x.Owner, owner));
        _enabled.Remove(owner);
    }

    private static bool Same(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Notifica a los consumidores (llamar tras cargar o togglear).</summary>
    public void RaiseChanged() => Changed?.Invoke();

    public void Clear()
    {
        _previewAssets.Clear();
        _commands.Clear();
        _commandGroups.Clear();
        _markdown.Clear();
        _panels.Clear();
        _listSettings.Clear();
        _vaultHandlers.Clear();
        _enabled.Clear();
    }
}
