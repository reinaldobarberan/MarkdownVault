using System.IO;
using MarkdownVault.PluginSdk;

namespace MarkdownVault.Services.Plugins;

/// <summary>
/// Fachada de solo-lectura que el host expone a los plugins. Los proveedores
/// (tema/archivo activo/estado) se enchufan tras crear el ViewModel, porque el
/// registro de plugins ocurre en el arranque.
/// </summary>
public sealed class HostServices : IHostServices
{
    private readonly FileService _fileService;

    public HostServices(FileService fileService) => _fileService = fileService;

    public Func<bool>?    DarkThemeProvider  { get; set; }
    public Func<string?>? ActiveFileProvider { get; set; }
    public Action<string>? StatusSink        { get; set; }

    /// <summary>
    /// Delegate inyectado en composición (App.xaml.cs) que abre una ruta ABSOLUTA en el
    /// editor del host — mismo patrón que <see cref="StatusSink"/>. El marshaling al hilo
    /// de UI (si hace falta) es responsabilidad de quien lo inyecta, no de este tipo.
    /// </summary>
    public Action<string>? OpenFileAction { get; set; }

    public string? VaultRoot      => _fileService.VaultRoot;
    public string? ActiveFilePath => ActiveFileProvider?.Invoke();
    public bool    IsDarkTheme    => DarkThemeProvider?.Invoke() ?? false;

    public Task<string> ReadFileAsync(string relativePath)
    {
        var root = _fileService.VaultRoot;
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("No hay vault abierto.");

        // Bloquea el escape fuera del vault (path traversal) vía el helper compartido.
        var full = PathConfinement.ResolveWithin(root, relativePath);

        return _fileService.ReadFileAsync(full);
    }

    public void ShowStatus(string message) => StatusSink?.Invoke(message);

    public void OpenVaultFile(string relativePath)
    {
        var root = _fileService.VaultRoot;
        if (string.IsNullOrEmpty(root))
            return; // Sin vault abierto: no-op silencioso (no hay dónde resolver la ruta).

        string full;
        try
        {
            // Mismo helper compartido que ReadFileAsync: bloquea el escape del vault.
            full = PathConfinement.ResolveWithin(root, relativePath);
        }
        catch (UnauthorizedAccessException)
        {
            return; // Ruta fuera del vault: no-op silencioso, nunca lanza hacia el plugin.
        }

        if (!File.Exists(full))
            return; // Archivo inexistente: no-op silencioso.

        OpenFileAction?.Invoke(full);
    }
}
