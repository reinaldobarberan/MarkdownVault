using MarkdownVault.PluginSdk;

namespace MarkdownVault.Services.Plugins;

/// <summary>
/// Vista POR PLUGIN de la fachada del host. Delega todo en el
/// <see cref="HostServices"/> compartido salvo una cosa:
/// <see cref="BeginProgress"/>, que estampa el id del plugin dueño en el scope.
///
/// ¿Por qué hace falta un decorador y no alcanza con el <see cref="HostServices"/>
/// compartido? Porque <see cref="HostServices"/> es UNA sola instancia para TODOS
/// los plugins (ver <see cref="PluginManager"/>): no tiene forma de saber quién
/// está llamando. Y sin saber el dueño no se puede cumplir la garantía dura del
/// contrato: <b>al desactivar un plugin hay que cerrar TODO scope suyo</b>
/// (<see cref="PluginProgressCoordinator.CloseAllFor"/>), o su barra queda colgada
/// para siempre y su trabajo de fondo sigue corriendo sin señal de corte.
///
/// El id sale de <c>plugin.json</c> vía <see cref="PluginMetadata"/>, igual que la
/// etiqueta de dueño que ya usa <see cref="PluginRegistry"/> para
/// <c>RemoveByOwner</c>. Un plugin no puede falsificarlo: nunca lo escribe él.
///
/// Nota de descarga en caliente: este decorador es un tipo del HOST y solo guarda
/// una referencia a otro tipo del host más un string. No retiene nada definido por
/// el plugin, así que vivir mientras el plugin está activo no clava su
/// <c>AssemblyLoadContext</c>.
/// </summary>
internal sealed class PluginHostServices : IHostServices
{
    private readonly IHostServices              _inner;
    private readonly string                     _ownerId;
    private readonly PluginProgressCoordinator? _progress;

    public PluginHostServices(IHostServices inner, string ownerId, PluginProgressCoordinator? progress)
    {
        _inner    = inner;
        _ownerId  = ownerId;
        _progress = progress;
    }

    public string? VaultRoot      => _inner.VaultRoot;
    public string? ActiveFilePath => _inner.ActiveFilePath;
    public bool    IsDarkTheme    => _inner.IsDarkTheme;

    public Task<string> ReadFileAsync(string relativePath) => _inner.ReadFileAsync(relativePath);
    public void ShowStatus(string message)                 => _inner.ShowStatus(message);
    public void OpenVaultFile(string relativePath)         => _inner.OpenVaultFile(relativePath);

    /// <summary>
    /// Sin coordinador conectado (pruebas, o un host armado a mano) se cae al
    /// comportamiento del facade interno, que a su vez devuelve
    /// <see cref="NoOpProgressScope.Instance"/>. NUNCA devuelve null: el contrato
    /// del SDK dice que el plugin no tiene que comprobar nada.
    /// </summary>
    public IProgressScope BeginProgress(string title)
        => _progress?.Begin(_ownerId, title) ?? _inner.BeginProgress(title);
}
