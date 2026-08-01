using MarkdownVault.PluginSdk;

namespace MarkdownVault.Services.Plugins;

public enum PluginState { Discovered, Active, Failed }

/// <summary>
/// Un plugin descubierto más su estado en runtime. Es lo que consumirá la
/// futura sección de UI para listar / activar / desactivar / mostrar fallidos.
/// </summary>
public sealed class PluginDescriptor
{
    public required PluginMetadata Metadata     { get; init; }
    public required string         FolderPath   { get; init; }
    public required string         EntryDllPath { get; init; }
    public PluginState             State        { get; set; } = PluginState.Discovered;
    public string?                 Error        { get; set; }

    /// <summary>Toggle del usuario (independiente de <see cref="State"/>). Un plugin
    /// Failed nunca puede quedar habilitado.</summary>
    public bool                    Enabled      { get; set; }
}
