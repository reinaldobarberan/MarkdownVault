namespace MarkdownVault.PluginSdk;

/// <summary>
/// Persistencia sandbox por plugin, expuesta vía <see cref="IPluginContext"/>.
/// Cada plugin ve únicamente su propia carpeta (<c>PluginData/&lt;plugin-id&gt;/</c>);
/// nunca el vault. Toda ruta relativa que resuelva fuera de esa carpeta lanza
/// <see cref="UnauthorizedAccessException"/> antes de cualquier I/O.
/// </summary>
public interface IPluginStorage
{
    /// <summary>Raíz absoluta del sandbox de este plugin. Puede no existir aún en disco.</summary>
    string RootPath { get; }

    /// <summary>Lee el texto completo en <paramref name="relativePath"/>. Lanza si no existe.</summary>
    Task<string> ReadTextAsync(string relativePath);

    /// <summary>
    /// Escribe (reemplazando por completo) el texto en <paramref name="relativePath"/>,
    /// creando la raíz del sandbox y cualquier subcarpeta intermedia si hace falta.
    /// </summary>
    Task WriteTextAsync(string relativePath, string content);

    /// <summary>Indica si existe un archivo en <paramref name="relativePath"/>.</summary>
    bool Exists(string relativePath);

    /// <summary>Borra el archivo en <paramref name="relativePath"/>. Idempotente: no lanza si no existe.</summary>
    void Delete(string relativePath);
}
