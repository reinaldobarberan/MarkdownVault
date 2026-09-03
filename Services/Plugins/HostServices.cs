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
    /// <summary>
    /// Dueño con el que se etiquetan los scopes abiertos contra ESTA fachada
    /// compartida, es decir los que no pasaron por el decorador por plugin
    /// (<see cref="PluginHostServices"/>). En la app real no debería haber ninguno:
    /// un id reservado y feo hace que salte a la vista si aparece uno.
    /// </summary>
    public const string HostOwnerId = "__host__";

    private readonly FileService                _fileService;
    private readonly PluginProgressCoordinator? _progress;

    public HostServices(FileService fileService, PluginProgressCoordinator? progress = null)
    {
        _fileService = fileService;
        _progress    = progress;
    }

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

    /// <summary>
    /// SDK 1.5.0. Delega tal cual en <see cref="FileService.GetOwningRoot"/>, que ya
    /// resuelve el prefijo más largo con raíces anidadas y nunca lanza — la MISMA
    /// función con la que MainWindow decide a qué carpeta mapear <c>vault.local</c>.
    /// Que sea la misma es el punto: un plugin que arme rutas con esto no se puede
    /// desincronizar de lo que la vista previa va a resolver.
    /// </summary>
    public string? GetOwningRoot(string path) =>
        string.IsNullOrWhiteSpace(path) ? null : _fileService.GetOwningRoot(path);

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

    /// <summary>
    /// Canal de progreso (SDK 1.3.0). El marshaling al hilo de UI lo hace el
    /// coordinador con el delegate que le inyectan en composición — mismo criterio
    /// que <see cref="StatusSink"/>, y por eso este tipo no conoce el Dispatcher.
    ///
    /// Los plugins NO llegan por acá: reciben la vista decorada
    /// (<see cref="PluginHostServices"/>) que estampa su id. Esta implementación
    /// existe porque el contrato la exige y para que un host armado a mano (una
    /// prueba) siga siendo utilizable.
    /// </summary>
    public IProgressScope BeginProgress(string title)
        => _progress?.Begin(HostOwnerId, title) ?? NoOpProgressScope.Instance;

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
