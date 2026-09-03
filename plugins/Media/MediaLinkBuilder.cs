using System.IO;
namespace MarkdownVault.Plugin.Media;

/// <summary>
/// Qué se inserta en el documento después de elegir un archivo en el diálogo, o
/// por qué no se puede insertar nada. Uno de los dos campos viene con valor;
/// nunca los dos.
/// </summary>
public readonly record struct MediaLinkResult(string? Markdown, string? Error)
{
    public bool Ok => Markdown is not null;

    public static MediaLinkResult Success(string markdown) => new(markdown, null);
    public static MediaLinkResult Failure(string error)     => new(null, error);
}

/// <summary>
/// Arma el enlace Markdown a partir del archivo que el usuario eligió en el
/// diálogo. Lógica PURA —sin diálogos, sin disco, sin WPF— para poder probar el
/// caso que importa de verdad: qué pasa cuando el archivo está FUERA del vault.
/// </summary>
public static class MediaLinkBuilder
{
    /// <summary>
    /// Traduce una ruta absoluta del disco al enlace relativo que la vista previa
    /// sabe resolver.
    ///
    /// La ruta se calcula contra la RAÍZ DEL VAULT y no contra la carpeta de la
    /// nota: el host inyecta <c>&lt;base href="http://vault.local/"&gt;</c> y mapea
    /// ese host a la raíz, así que todo destino relativo se resuelve desde ahí.
    /// Es el mismo criterio con el que el host inserta las imágenes pegadas
    /// (<c>attachments/{archivo}</c>, en EditorView).
    /// </summary>
    /// <param name="vaultRoot">Raíz del vault abierto, o <c>null</c> si no hay ninguno.</param>
    /// <param name="absolutePath">Lo que devolvió el diálogo.</param>
    /// <param name="altText">
    /// Texto para los corchetes. Si viene vacío se usa el nombre del archivo sin
    /// extensión, que es lo que el usuario reconoce de un vistazo.
    /// </param>
    public static MediaLinkResult Build(string? vaultRoot, string absolutePath, string? altText = null)
    {
        if (string.IsNullOrWhiteSpace(vaultRoot))
            return MediaLinkResult.Failure(
                "No hay ningún vault abierto: la vista previa no sabría dónde buscar el archivo.");

        if (string.IsNullOrWhiteSpace(absolutePath))
            return MediaLinkResult.Failure("No se eligió ningún archivo.");

        string relative;
        try
        {
            relative = Path.GetRelativePath(vaultRoot, absolutePath);
        }
        catch (ArgumentException)
        {
            return MediaLinkResult.Failure($"La ruta «{absolutePath}» no es válida.");
        }

        // Fuera del vault no hay nada que hacer, y conviene decirlo CLARO en vez de
        // insertar un enlace que se ve bien y no reproduce: el host sirve la vista
        // previa por vault.local, que está mapeado a la raíz del vault y a nada más.
        // Un archivo de afuera simplemente no se sirve.
        //
        // Copiarlo al vault sería lo cómodo, pero el plugin NO puede: IHostServices
        // es de solo lectura a propósito, y escribir en el vault por la espalda del
        // host se saltearía su confinamiento de rutas.
        if (IsOutsideRoot(relative))
            return MediaLinkResult.Failure(
                "Ese archivo está fuera del vault y la vista previa no lo puede abrir. " +
                "Copialo dentro del vault (por ejemplo a la carpeta «attachments») y volvé a elegirlo.");

        var href = relative.Replace('\\', '/');

        // CommonMark: un destino con espacios o paréntesis tiene que ir entre
        // ángulos, si no el enlace no se reconoce. Misma regla que aplica el host
        // al convertir wikilinks (MarkdownService.ConvertWikiLinks).
        if (href.IndexOfAny([' ', '(', ')']) >= 0)
            href = $"<{href}>";

        var alt = string.IsNullOrWhiteSpace(altText)
            ? Path.GetFileNameWithoutExtension(absolutePath)
            : altText.Trim();

        return MediaLinkResult.Success($"![{EscapeAlt(alt)}]({href})");
    }

    /// <summary>
    /// <see cref="Path.GetRelativePath"/> devuelve algo que empieza con <c>..</c>
    /// cuando hay que subir, y devuelve la ruta ABSOLUTA tal cual cuando ni siquiera
    /// comparten unidad (C: contra D:). Los dos casos son "afuera".
    /// </summary>
    private static bool IsOutsideRoot(string relative)
    {
        if (Path.IsPathRooted(relative)) return true;

        // Ojo con el prefijo suelto: una carpeta del vault llamada "..copias" empieza
        // con ".." y NO está afuera. Lo que marca la salida es que los dos puntos sean
        // el segmento entero.
        return relative == ".."
            || relative.StartsWith("../", StringComparison.Ordinal)
            || relative.StartsWith(@"..\", StringComparison.Ordinal);
    }

    /// <summary>
    /// Un corchete suelto en el nombre del archivo partiría el enlace en dos. Se
    /// escapan, que es lo que CommonMark espera, en vez de borrarlos: el nombre que
    /// ve el usuario sigue siendo el suyo.
    /// </summary>
    private static string EscapeAlt(string alt) =>
        alt.Replace("\\", "\\\\")
           .Replace("[", "\\[")
           .Replace("]", "\\]");
}
