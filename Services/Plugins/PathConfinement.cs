using System.IO;

namespace MarkdownVault.Services.Plugins;

/// <summary>
/// Helper compartido de confinamiento de rutas: normaliza <paramref name="root"/> y
/// <c>root + rel</c> con <see cref="Path.GetFullPath(string)"/> y exige que la ruta
/// resultante quede DENTRO de <paramref name="root"/> (comparación de prefijo con
/// separador final, para evitar bypass por prefijos hermanos como "a" vs "ab").
/// Usado tanto por <c>HostServices.ReadFileAsync</c> (vault) como por
/// <c>PluginStorage</c> (sandbox por plugin).
/// </summary>
/// <remarks>
/// LIMITACIÓN: la resolución es puramente léxica (<see cref="Path.GetFullPath(string)"/>);
/// no resuelve symlinks/junctions. Coincide con la garantía ya probada de
/// <c>HostServices.ReadFileAsync</c> bajo el modelo de confianza first-party actual.
/// </remarks>
internal static class PathConfinement
{
    /// <summary>
    /// Resuelve <paramref name="rel"/> dentro de <paramref name="root"/> y lanza
    /// <see cref="UnauthorizedAccessException"/> si la ruta normalizada queda fuera.
    /// </summary>
    public static string ResolveWithin(string root, string rel)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, rel));

        if (!full.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("La ruta está fuera del área permitida.");

        return full;
    }
}
