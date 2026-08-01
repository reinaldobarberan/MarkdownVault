using System.IO;
using System.Text;
using MarkdownVault.PluginSdk;

namespace MarkdownVault.Services.Plugins;

/// <summary>
/// Implementación host de <see cref="IPluginStorage"/>: persistencia sandbox por
/// plugin bajo un <see cref="RootPath"/> inyectado (en producción,
/// <c>%AppData%/MarkdownVault/PluginData/&lt;plugin-id&gt;/</c>, construido por
/// <c>PluginManager</c>). La raíz se crea de forma PEREZOSA: solo
/// <see cref="WriteTextAsync"/> llama <see cref="Directory.CreateDirectory(string)"/>;
/// leer, comprobar existencia o borrar nunca la crean. Toda ruta relativa se resuelve
/// vía <see cref="PathConfinement.ResolveWithin"/> ANTES de tocar el disco.
/// </summary>
public sealed class PluginStorage : IPluginStorage
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public string RootPath { get; }

    public PluginStorage(string rootPath) => RootPath = rootPath;

    public Task<string> ReadTextAsync(string relativePath)
    {
        var full = PathConfinement.ResolveWithin(RootPath, relativePath);

        if (!File.Exists(full))
            throw new FileNotFoundException("El archivo no existe en el almacenamiento del plugin.", full);

        return File.ReadAllTextAsync(full, Utf8NoBom);
    }

    public Task WriteTextAsync(string relativePath, string content)
    {
        var full = PathConfinement.ResolveWithin(RootPath, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return File.WriteAllTextAsync(full, content, Utf8NoBom);
    }

    public bool Exists(string relativePath)
    {
        var full = PathConfinement.ResolveWithin(RootPath, relativePath);
        return File.Exists(full);
    }

    public void Delete(string relativePath)
    {
        var full = PathConfinement.ResolveWithin(RootPath, relativePath);

        if (File.Exists(full))
            File.Delete(full);
    }
}
