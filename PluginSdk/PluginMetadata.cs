namespace MarkdownVault.PluginSdk;

/// <summary>Metadata de un plugin, resuelta desde su plugin.json.</summary>
public sealed class PluginMetadata
{
    public string Id          { get; init; } = "";
    public string Name        { get; init; } = "";
    public string Version     { get; init; } = "";
    public string Description { get; init; } = "";
    public string Author      { get; init; } = "";
    public string MinSdk      { get; init; } = "1.0.0";
}

/// <summary>Versión del contrato del SDK. Los plugins declaran su <c>minSdk</c> contra esto.</summary>
public static class SdkInfo
{
    public const string Version = "1.2.0";
}
