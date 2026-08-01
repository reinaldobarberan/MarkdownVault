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

    public HostPluginContext(
        PluginMetadata metadata, IHostServices host, PluginRegistry registry, string baseDir,
        IPluginStorage storage)
    {
        Metadata  = metadata;
        Host      = host;
        _registry = registry;
        _baseDir  = baseDir;
        Storage   = storage;
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
    public void OnVaultEvent(Action<VaultEvent> h)    => _registry.AddVaultHandler(Metadata.Id, h);

    public void Log(string message)
        => System.Diagnostics.Debug.WriteLine($"[plugin:{Metadata.Id}] {message}");

    // Reutiliza el cableado existente PluginRegistry.Changed -> App.xaml.cs
    // Dispatcher.Invoke(Editor.RefreshPreviewFromPlugins()); no hay mecanismo paralelo.
    public void RequestPreviewRefresh()
        => _registry.RaiseChanged();
}
