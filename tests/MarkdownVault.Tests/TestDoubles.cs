using MarkdownVault.PluginSdk;

namespace MarkdownVault.Tests;

/// <summary>Fachada de host mínima para pruebas (no toca disco ni vault).</summary>
internal sealed class FakeHost : IHostServices
{
    public string? VaultRoot      => null;
    public string? ActiveFilePath => null;
    public bool    IsDarkTheme    => false;
    public Task<string> ReadFileAsync(string relativePath) => Task.FromResult(string.Empty);
    public void ShowStatus(string message) { }
    public void OpenVaultFile(string relativePath) { }
}

/// <summary>Contribución Markdown de prueba (identidad por referencia).</summary>
internal sealed class FakeMarkdownContribution : IMarkdownContribution
{
    public object CreateMarkdigExtension() => new object();
}
