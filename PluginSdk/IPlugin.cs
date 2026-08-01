namespace MarkdownVault.PluginSdk;

/// <summary>
/// Contrato mínimo de un plugin. El host instancia esta clase (constructor sin
/// parámetros) y llama <see cref="Configure"/> una vez, al activar el plugin.
/// La metadata (id, nombre, versión) NO se declara aquí: vive en plugin.json.
/// </summary>
public interface IPlugin
{
    /// <summary>
    /// Se llama una vez cuando el plugin se activa. Aquí el plugin registra TODAS
    /// sus contribuciones a través del contexto. No hagas I/O pesado ni bloqueante.
    /// </summary>
    void Configure(IPluginContext context);
}

/// <summary>Ciclo de vida opcional para plugins que necesitan inicializar/liberar recursos.</summary>
public interface IActivatablePlugin : IPlugin
{
    Task OnActivatedAsync();
    Task OnDeactivatedAsync();
}
