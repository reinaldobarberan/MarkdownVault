using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdownVault.Services;

namespace MarkdownVault.Helpers;

/// <summary>
/// Pure resolution of a parsed <see cref="LinkTarget"/>'s anchor to a SOURCE LINE inside an
/// already-parsed <see cref="MarkdownDocument"/> — no WPF, no file I/O, no AvalonEdit. The
/// document must come from <c>MarkdownService.ParsePreviewAst</c> (the SAME pipeline instance
/// used for rendering) so heading ids match exactly what the preview shows (design decision #6).
/// </summary>
/// <remarks>
/// POR QUÉ LÍNEA Y NO OFFSET (el bug que originó este archivo tal como está hoy):
/// <c>ParsePreviewAst</c> parsea el texto REESCRITO por <c>PreprocessWikiLinks</c>, no el que
/// el usuario tiene en el editor. Un <c>[[nota#^id]]</c> se expande a un enlace Markdown
/// completo, mucho más largo, y a partir de ahí todos los <c>SourceSpan</c> del AST quedan
/// corridos: son índices en la cadena reescrita. Devolver <c>Span.Start</c> y pasárselo a
/// <c>SelectAndReveal</c> no tiraba excepción — simplemente aterrizaba en el lugar equivocado,
/// EN SILENCIO, en cualquier nota con un wikilink antes del ancla.
///
/// La línea, en cambio, sobrevive: el preprocesado sustituye siempre dentro de una línea y
/// nunca agrega ni saca saltos (ver el regex de wikilink en <c>MarkdownService</c>). Por eso
/// este helper habla en LÍNEAS (base 0, como Markdig) y es la VISTA la que las convierte a
/// offset preguntándole al documento vivo — <c>GetLineByNumber(line + 1).Offset</c> — que por
/// construcción no puede estar corrido.
///
/// INVARIANTE: ningún offset derivado de este AST puede indexar el documento de AvalonEdit.
/// </remarks>
internal static class AnchorLocator
{
    /// <summary>
    /// Resolves <paramref name="target"/>'s anchor against <paramref name="doc"/>. Returns the
    /// 0-based SOURCE LINE to reveal (the caller turns it into a live-document offset), or
    /// <c>null</c> when the anchor kind is <see cref="AnchorKind.None"/> or nothing matches —
    /// the broken-anchor path, where the caller opens the note from the top and reports via
    /// <c>StatusSink</c> instead.
    /// </summary>
    public static int? Find(MarkdownDocument doc, LinkTarget target) => target.Kind switch
    {
        AnchorKind.Heading => FindHeading(doc, target.Anchor),
        AnchorKind.Block   => FindBlock(doc, target.Anchor),
        _                  => null,
    };

    /// <summary>
    /// Two-pass lookup: first an exact match against the resolved id (covers
    /// <c>[text](#slug)</c>-style links, already lowercase-slug-shaped), then a
    /// case-insensitive match against the heading's own plain text (covers Obsidian-style links
    /// that spell out the visible heading, e.g. <c>[[Nota#Instalación]]</c> even where GitHub's
    /// slugger and the written text happen to diverge).
    /// </summary>
    private static int? FindHeading(MarkdownDocument doc, string? anchor)
    {
        if (string.IsNullOrEmpty(anchor)) return null;

        HeadingBlock? matchedByText = null;
        foreach (var heading in doc.Descendants<HeadingBlock>())
        {
            var id = heading.TryGetAttributes()?.Id;
            if (id is not null && string.Equals(id, anchor, StringComparison.Ordinal))
                return heading.Line;

            if (matchedByText is null &&
                string.Equals(HeadingText(heading), anchor, StringComparison.OrdinalIgnoreCase))
                matchedByText = heading;
        }
        return matchedByText?.Line;
    }

    /// <summary>Matches the namespaced id (<see cref="BlockAnchorExtension.IdPrefix"/> + bare id).</summary>
    private static int? FindBlock(MarkdownDocument doc, string? anchor)
    {
        if (string.IsNullOrEmpty(anchor)) return null;
        var namespacedId = BlockAnchorExtension.IdPrefix + anchor;

        foreach (var paragraph in doc.Descendants<ParagraphBlock>())
        {
            var id = paragraph.TryGetAttributes()?.Id;
            if (string.Equals(id, namespacedId, StringComparison.Ordinal))
                return paragraph.Line;
        }
        return null;
    }

    // ─── Line geometry (para "Copiar enlace a este párrafo") ─────────────────────────────

    /// <summary>
    /// Primera línea (base 0) de cualquier bloque que EMPIECE después de
    /// <paramref name="block"/>, o <see cref="int.MaxValue"/> si no hay ninguno. Es el límite
    /// superior EXCLUSIVO del bloque en líneas.
    /// </summary>
    /// <remarks>
    /// Se calcula con los <c>Line</c> de los bloques hermanos/anidados en vez de con
    /// <c>Span</c> (corrido, ver el remark de la clase) y en vez de con
    /// <c>LeafBlock.Lines.Count</c> (que Markdig libera tras procesar inlines, así que no es
    /// confiable después del parseo completo). El filtro <c>Line &gt; block.Line</c> descarta a
    /// los contenedores que arrancan en la MISMA línea que el bloque (un <c>ListBlock</c> y su
    /// <c>ListItemBlock</c> y su párrafo comparten línea de inicio).
    /// </remarks>
    public static int NextBlockStartLine(MarkdownDocument doc, Block block)
    {
        var next = int.MaxValue;
        foreach (var other in doc.Descendants<Block>())
            if (other.Line > block.Line && other.Line < next)
                next = other.Line;
        return next;
    }

    /// <summary>
    /// El párrafo que ocupa <paramref name="line"/> (base 0) o, si ninguno la contiene, el más
    /// cercano en líneas. "Más cercano" y no "exacto" porque un clic derecho puede caer en una
    /// línea en blanco o entre bloques y la intención ("este párrafo") sigue siendo obvia.
    /// </summary>
    /// <remarks>
    /// Antes esto comparaba el offset del clic — del documento VIVO — contra
    /// <c>paragraph.Span</c> — del texto REESCRITO. Con un wikilink antes del cursor elegía
    /// otro párrafo sin avisar, y después escribía el marcador ahí. Ahora la comparación es
    /// línea contra línea, que es la única coordenada compartida entre ambos textos.
    /// </remarks>
    public static ParagraphBlock? FindParagraphAtLine(MarkdownDocument doc, int line)
    {
        ParagraphBlock? nearest         = null;
        var             nearestDistance = int.MaxValue;

        foreach (var paragraph in doc.Descendants<ParagraphBlock>())
        {
            var start = paragraph.Line;
            var end   = NextBlockStartLine(doc, paragraph) - 1;   // inclusivo, puede ser MaxValue-1
            if (line >= start && line <= end) return paragraph;

            var distance = line < start ? start - line : line - end;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest         = paragraph;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Última línea (base 0) que <paramref name="block"/> ocupa DE VERDAD en el documento vivo
    /// — donde va a parar un marcador <c>^id</c> nuevo. Se parte del límite que marca el
    /// siguiente bloque del AST (en LÍNEAS, que sí sobreviven al preprocesado), se recorta
    /// contra el largo real del buffer y se retrocede sobre las líneas en blanco finales, así
    /// el marcador queda pegado al último renglón con texto incluso cuando al párrafo lo corta
    /// un heading sin línea en blanco de por medio.
    /// </summary>
    /// <param name="liveLineCount">Cantidad de líneas del documento VIVO, no del AST.</param>
    /// <param name="liveLineText">Texto de una línea del documento VIVO, índice base 0.</param>
    public static int ParagraphLastLine(
        MarkdownDocument doc, ParagraphBlock block, int liveLineCount, Func<int, string> liveLineText)
    {
        var liveLast = liveLineCount - 1;
        if (liveLast < 0) return 0;

        var start = Math.Clamp(block.Line, 0, liveLast);
        var bound = Math.Min(NextBlockStartLine(doc, block) - 1, liveLast);

        for (var l = bound; l > start; l--)
            if (!string.IsNullOrWhiteSpace(liveLineText(l)))
                return l;

        return start;
    }

    private static string HeadingText(HeadingBlock heading)
    {
        if (heading.Inline is null) return string.Empty;
        var sb = new System.Text.StringBuilder();
        foreach (var literal in heading.Inline.Descendants<LiteralInline>())
            sb.Append(literal.Content.ToString());
        return sb.ToString();
    }
}
