using MarkdownVault.PluginSdk;
using MarkdownVault.Services;

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

/// <summary>Scripted <see cref="IDialogService"/> double: no real dialogs, records every call.</summary>
internal sealed class FakeDialogService : IDialogService
{
    public ConfirmResult ConfirmResult { get; set; } = ConfirmResult.No;
    public string? SavePathResult { get; set; }
    public string? ImagePathResult { get; set; }
    public string? LinkMarkdownResult { get; set; }
    public List<string> Errors { get; } = new();
    public List<string> Infos  { get; } = new();
    public int ConfirmCount { get; private set; }

    public ConfirmResult ConfirmSaveChanges(string fileName) { ConfirmCount++; return ConfirmResult; }
    public void ShowError(string m, string t) => Errors.Add(m);
    public void ShowInfo (string m, string t) => Infos.Add(m);
    public string? AskSaveFilePath(string s, string f, string d) => SavePathResult;
    public string? AskImagePath() => ImagePathResult;
    public string? PickInternalLinkMarkdown(IReadOnlyList<string> f, string c, string v) => LinkMarkdownResult;
}

/// <summary>Contribución Markdown de prueba (identidad por referencia).</summary>
internal sealed class FakeMarkdownContribution : IMarkdownContribution
{
    public object CreateMarkdigExtension() => new object();
}
