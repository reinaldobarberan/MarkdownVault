using MarkdownVault.PluginSdk;

namespace MarkdownVault.Services.Plugins;

/// <summary>
/// Implementación de <see cref="IPluginContext"/> que el host entrega a cada
/// plugin durante <see cref="IPlugin.Configure"/>. Redirige los registros al
/// <see cref="PluginRegistry"/> y resuelve las rutas de assets empaquetados
/// contra la carpeta del plugin.
/// </summary>
internal sealed class HostPluginContext : IPluginContext
{
    private readonly PluginRegistry _registry;
    private readonly string         _baseDir;
    private readonly IPluginLogSink _logSink;

    public HostPluginContext(
        PluginMetadata metadata, IHostServices host, PluginRegistry registry, string baseDir,
        IPluginStorage storage,
        PluginProgressCoordinator? progress = null, IPluginLogSink? logSink = null)
    {
        Metadata  = metadata;
        // El plugin NO recibe la fachada compartida sino una vista decorada que
        // estampa su id en los scopes de progreso — así el host puede cerrarlos
        // todos al desactivarlo (ver PluginHostServices y §9 de la guía).
        Host      = new PluginHostServices(host, metadata.Id, progress);
        _registry = registry;
        _baseDir  = baseDir;
        Storage   = storage;
        _logSink  = logSink ?? NullPluginLogSink.Instance;
    }

    public IHostServices  Host     { get; }
    public IPluginStorage Storage  { get; }
    public PluginMetadata Metadata { get; }

    public void AddMarkdownExtension(IMarkdownContribution extension, int order = 0)
        => _registry.AddMarkdownContribution(Metadata.Id, extension, order);

    public void AddPreviewAsset(PreviewAsset asset)
    {
        // Los assets empaquetados se resuelven a ruta absoluta ya mismo, para que
        // el renderizador solo tenga que leerlos.
        if (asset.Source == AssetSource.BundledFile)
        {
            asset = new PreviewAsset
            {
                Kind      = asset.Kind,
                Source    = asset.Source,
                Value     = System.IO.Path.Combine(_baseDir, asset.Value),
                Placement = asset.Placement
            };
        }
        _registry.AddPreviewAsset(Metadata.Id, asset);
    }

    public void AddCommand(PluginCommand command)          => _registry.AddCommand(Metadata.Id, command);
    public void AddCommandGroup(PluginCommandGroup group)  => _registry.AddCommandGroup(Metadata.Id, group);
    public void AddPanel(PluginPanel panel)                => _registry.AddPanel(Metadata.Id, panel);
    public void AddListSetting(PluginListSetting setting)  => _registry.AddListSetting(Metadata.Id, setting);
    public void OnVaultEvent(Action<VaultEvent> h)    => _registry.AddVaultHandler(Metadata.Id, h);

    /// <summary>
    /// Va a DOS destinos. <c>Debug.WriteLine</c> se conserva (cómodo con el
    /// depurador enganchado) pero por sí solo era un pozo: no se ve sin depurador
    /// y desaparece entero en Release por <c>[Conditional("DEBUG")]</c>. El sumidero
    /// a archivo (<see cref="FilePluginLogSink"/>, %AppData%/MarkdownVault/logs/)
    /// es el que hace el log realmente legible. Nunca lanza hacia el plugin.
    /// </summary>
    public void Log(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[plugin:{Metadata.Id}] {message}");
        _logSink.Write(Metadata.Id, message);
    }

    // Reutiliza el cableado existente PluginRegistry.Changed -> App.xaml.cs
    // Dispatcher.Invoke(Editor.RefreshPreviewFromPlugins()); no hay mecanismo paralelo.
    public void RequestPreviewRefresh()
        => _registry.RaiseChanged();
}
