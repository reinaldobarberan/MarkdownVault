using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax.Inlines;

namespace MarkdownVault.Plugin.Media;

/// <summary>
/// Extensión de Markdig que registra <see cref="MediaLinkRenderer"/> para
/// interceptar las IMÁGENES cuyo destino es un archivo de video o audio. No aporta
/// sintaxis nueva al parser: <c>![](demo.mp4)</c> ya es Markdown estándar — solo
/// cambia CÓMO se renderiza ese enlace a HTML.
///
/// Por qué alcanza con interceptar imágenes: el host reescribe los wikilinks ANTES
/// de Markdig (<c>MarkdownService.PreprocessWikiLinks</c>), y su regex solo captura
/// <c>[[…]]</c> — el <c>!</c> de adelante queda intacto. Por eso
/// <c>![[demo.mp4]]</c> termina como <c>![demo](demo.mp4)</c>: las DOS sintaxis
/// desembocan en el mismo nodo, un <see cref="LinkInline"/> con
/// <c>IsImage = true</c>. Un solo punto de intercepción, cero cambios en el núcleo.
/// </summary>
public sealed class MediaMarkdigExtension : IMarkdownExtension
{
    private readonly Func<MediaFormats> _formats;

    /// <param name="formats">
    /// Se recibe como función, NO como valor: el usuario puede editar los formatos
    /// desde la ventana de Complementos y el pipeline de Markdig queda cacheado en
    /// <c>MarkdownService</c> hasta que cambie el set de plugins. Con un valor fijo,
    /// editar la lista no surtiría efecto hasta reiniciar.
    /// </param>
    public MediaMarkdigExtension(Func<MediaFormats> formats)
    {
        _formats = formats;
    }

    /// <summary>No-op: no hace falta ningún cambio en el parser, solo en el render.</summary>
    public void Setup(MarkdownPipelineBuilder pipeline) { }

    /// <summary>
    /// Inserta nuestro renderer PRIMERO en la cadena (índice 0).
    /// </summary>
    /// <remarks>
    /// Mismo comportamiento verificado que documenta el plugin Eisenhower: Markdig
    /// 1.1.2 despacha por asignabilidad de TIPO, no por contenido, y <c>Accept</c> es
    /// <c>sealed</c>. Insertar en el índice 0 NO da un "fallback automático al
    /// siguiente renderer": este pasa a ser el único candidato para TODO
    /// <see cref="LinkInline"/> — enlaces comunes e imágenes incluidos. Por eso el
    /// filtrado y el fallback viven DENTRO de <see cref="MediaLinkRenderer.Write"/>,
    /// y por eso el renderer hereda de <see cref="LinkInlineRenderer"/> en vez de
    /// componerlo: así <c>base.Write</c> es el comportamiento estándar de Markdig,
    /// sin reimplementarlo ni depender de la visibilidad de un miembro ajeno.
    /// </remarks>
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        renderer.ObjectRenderers.Insert(0, new MediaLinkRenderer(_formats));
    }
}

/// <summary>
/// Renderer de enlaces que convierte <c>![](clip.mp4)</c> en <c>&lt;video controls&gt;</c>
/// y <c>![](nota.mp3)</c> en <c>&lt;audio controls&gt;</c>. Todo lo demás —enlaces
/// normales, imágenes de verdad, URLs remotas— se delega sin cambios al
/// <see cref="LinkInlineRenderer"/> estándar vía <c>base.Write</c>.
/// </summary>
public sealed class MediaLinkRenderer : LinkInlineRenderer
{
    /// <summary>Clase que comparten todos los reproductores: la usan el CSS y el JS del plugin.</summary>
    public const string CssClass = "mv-media";

    private readonly Func<MediaFormats> _formats;

    public MediaLinkRenderer(Func<MediaFormats> formats)
    {
        _formats = formats;
    }

    protected override void Write(HtmlRenderer renderer, LinkInline link)
    {
        // Un enlace común (sin '!') sigue siendo un enlace: si alguien escribe
        // [mirá el clip](demo.mp4), quiere un enlace, no un reproductor incrustado.
        if (!link.IsImage) { base.Write(renderer, link); return; }

        // Contexto sin HTML (p. ej. mientras se arma el alt de otra imagen): ahí no
        // se pueden emitir etiquetas. Markdig ya sabe qué hacer.
        if (!renderer.EnableHtmlForInline) { base.Write(renderer, link); return; }

        var url  = link.GetDynamicUrl?.Invoke() ?? link.Url;
        var kind = _formats().Resolve(url);
        if (kind == MediaKind.None) { base.Write(renderer, link); return; }

        var tag = kind == MediaKind.Video ? "video" : "audio";

        renderer.Write('<').Write(tag);
        renderer.Write(" class=\"").Write(CssClass).Write('"');
        renderer.Write(" controls");
        // preload="metadata" trae la duración para dibujar la barra, pero NO el
        // archivo entero: una nota con cinco videos de 200 MB no puede costar 1 GB
        // de disco leído cada vez que se re-renderiza la vista previa.
        renderer.Write(" preload=\"metadata\"");
        renderer.Write(" src=\"");
        renderer.WriteEscapeUrl(url);
        renderer.Write('"');

        // Atributos del usuario ({#id .clase width=…}), si la extensión de
        // atributos genéricos está activa. Mismo trato que le da Markdig a <img>.
        renderer.WriteAttributes(link);
        renderer.Write('>');

        // El texto del corchete pasa a ser el contenido de respaldo: es lo que ve
        // quien no puede reproducirlo. Se escribe SIN HTML, igual que Markdig hace
        // con el alt de una imagen.
        bool htmlWasEnabled = renderer.EnableHtmlForInline;
        renderer.EnableHtmlForInline = false;
        renderer.WriteChildren(link);
        renderer.EnableHtmlForInline = htmlWasEnabled;

        renderer.Write("</").Write(tag).Write('>');
    }
}
