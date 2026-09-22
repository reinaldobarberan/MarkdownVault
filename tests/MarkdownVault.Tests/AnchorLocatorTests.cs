using ICSharpCode.AvalonEdit.Document;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using MarkdownVault.Helpers;
using MarkdownVault.Services;
using MarkdownVault.Services.Plugins;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Covers <see cref="AnchorLocator.Find"/> (heading by id, heading by text, block by id,
/// miss → null) AND, since they're the same pure boundary per design.md's Testing Strategy
/// table, <see cref="MarkdownService.GetHeadings"/> / <see cref="MarkdownService.GetBlockMarkers"/>
/// / <see cref="MarkdownService.ParsePreviewAst"/> directly. Real <c>Markdig.Markdown.Parse</c> via
/// <see cref="MarkdownService"/> (the SAME pipeline instance the app renders with — design
/// decision #6, "zero drift"), no WPF. <c>AnchorLocator.Find</c> is <c>internal</c>; visible here
/// via the pre-existing <c>InternalsVisibleTo("MarkdownVault.Tests")</c> in
/// <c>MarkdownVault.csproj</c> (already present — batch 1/2 flagged this as needed, it turned
/// out to already be there for the pre-existing test-hook use case, so no csproj change was
/// needed for this batch).
/// </summary>
public class AnchorLocatorTests
{
    private static MarkdownService NewMarkdownService() => new(new PluginRegistry());

    // ─── AnchorLocator.Find — heading by id ──────────────────────────────────────────────

    [Fact]
    public void Find_heading_by_exact_id_match()
    {
        var svc = NewMarkdownService();
        const string markdown = "## Instalacion\n\nTexto.";
        var doc = svc.ParsePreviewAst(markdown);
        var id = Assert.Single(svc.GetHeadings(markdown)).Id;

        // The id itself (already the lowercase GitHub slug) matches on pass 1 (ordinal id match).
        var target = new LinkTarget("Nota", null, id, AnchorKind.Heading);
        var offset = AnchorLocator.Find(doc, target);

        Assert.NotNull(offset);
    }

    [Fact]
    public void Find_heading_falls_back_to_case_insensitive_text_match_when_id_differs()
    {
        var svc = NewMarkdownService();
        // The id is lowercased by AutoIdentifierOptions.GitHub; the ORIGINAL heading text keeps
        // its casing, so an anchor written with the visible heading text (Obsidian style) fails
        // the ordinal id-pass and must fall through to the case-insensitive text-pass.
        const string markdown = "## Instalación\n\nTexto.";
        var doc = svc.ParsePreviewAst(markdown);

        var target = new LinkTarget("Nota", null, "Instalación", AnchorKind.Heading);
        var offset = AnchorLocator.Find(doc, target);

        Assert.NotNull(offset);
    }

    [Fact]
    public void Find_heading_returns_null_when_no_heading_matches()
    {
        var svc = NewMarkdownService();
        const string markdown = "## Instalación\n\nTexto.";
        var doc = svc.ParsePreviewAst(markdown);

        var target = new LinkTarget("Nota", null, "NoExiste", AnchorKind.Heading);

        Assert.Null(AnchorLocator.Find(doc, target));
    }

    // ─── AnchorLocator.Find — block by (namespaced) id ───────────────────────────────────

    [Fact]
    public void Find_block_by_marker_id()
    {
        var svc = NewMarkdownService();
        const string markdown = "Un párrafo marcado ^a1b2c3";
        var doc = svc.ParsePreviewAst(markdown);

        var target = new LinkTarget("Nota", null, "a1b2c3", AnchorKind.Block);
        var offset = AnchorLocator.Find(doc, target);

        Assert.NotNull(offset);
    }

    [Fact]
    public void Find_block_returns_null_when_the_marker_does_not_exist()
    {
        var svc = NewMarkdownService();
        const string markdown = "Un párrafo marcado ^a1b2c3";
        var doc = svc.ParsePreviewAst(markdown);

        var target = new LinkTarget("Nota", null, "noexiste", AnchorKind.Block);

        Assert.Null(AnchorLocator.Find(doc, target));
    }

    [Fact]
    public void Find_returns_null_for_AnchorKind_None()
    {
        var svc = NewMarkdownService();
        const string markdown = "## Instalación\n\nTexto.";
        var doc = svc.ParsePreviewAst(markdown);

        var target = new LinkTarget("Nota", null, null, AnchorKind.None);

        Assert.Null(AnchorLocator.Find(doc, target));
    }

    // ─── MarkdownService.GetHeadings / GetBlockMarkers / ParsePreviewAst — direct coverage ─────

    [Fact]
    public void GetHeadings_returns_id_text_and_line_for_every_heading_in_order()
    {
        var svc = NewMarkdownService();
        const string markdown = "# Título\n\nIntro.\n\n## Instalación\n\nTexto.";

        var headings = svc.GetHeadings(markdown);

        Assert.Equal(2, headings.Count);
        Assert.Equal("Título", headings[0].Text);
        Assert.Equal("Instalación", headings[1].Text);
        Assert.True(headings[0].Line < headings[1].Line);
        Assert.All(headings, h => Assert.False(string.IsNullOrEmpty(h.Id)));
    }

    [Fact]
    public void GetBlockMarkers_strips_the_namespace_prefix_back_to_the_bare_id()
    {
        var svc = NewMarkdownService();
        const string markdown = "Primer párrafo ^a1b2c3\n\nSegundo párrafo sin marca.";

        var markers = svc.GetBlockMarkers(markdown);

        var marker = Assert.Single(markers);
        Assert.Equal("a1b2c3", marker.Id); // bare — no "mv-b-" prefix
        Assert.DoesNotContain(BlockAnchorExtension.IdPrefix, marker.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void GetBlockMarkers_is_empty_when_the_note_has_zero_marked_paragraphs()
    {
        var svc = NewMarkdownService();
        const string markdown = "Solo texto, sin ningún marcador.";

        Assert.Empty(svc.GetBlockMarkers(markdown));
    }

    [Fact]
    public void ParsePreviewAst_uses_the_same_pipeline_GetHeadings_and_GetBlockMarkers_rely_on()
    {
        // decision #6: ParsePreviewAst must be the SAME pipeline instance used for rendering, so a
        // heading id resolved here is exactly what the preview's DOM id will be.
        var svc = NewMarkdownService();
        const string markdown = "## Instalación";

        var doc = svc.ParsePreviewAst(markdown);
        var headings = svc.GetHeadings(markdown);

        var heading = Assert.Single(doc.Descendants<HeadingBlock>());
        Assert.Equal(heading.TryGetAttributes()?.Id, headings[0].Id);
    }

    // ─── Regresión: offsets envenenados por el preprocesado de wikilinks ────────────────
    //
    // ParsePreviewAst parsea el texto REESCRITO por PreprocessWikiLinks, no el que el usuario
    // tiene en el editor. `[[audio parte 001#^8jlekw]]` (27 chars) se expande a
    // `[audio parte 001](<audio parte 001.md#^8jlekw>)` (47), así que todo SourceSpan posterior
    // queda corrido +20. Las LÍNEAS, en cambio, sobreviven: el preprocesado sustituye siempre
    // dentro de una línea.
    //
    // Estos tests fijan las dos consecuencias que se escaparon a producción:
    //   1. AnchorLocator devolvía Span.Start → el salto a un ancla aterrizaba mal EN SILENCIO.
    //   2. CopyParagraphLink insertaba en Span.End + 1 → ArgumentOutOfRangeException en notas
    //      cortas y marcador en el párrafo equivocado en notas largas.
    // Todas las aserciones se hacen contra el TEXTO CRUDO, nunca contra el reescrito.

    /// <summary>La forma exacta que reventó en la app del usuario.</summary>
    private const string CrashShape = "[[audio parte 001#^8jlekw]]pagina 1\npagina 2";

    private static int RawOffsetOfLine(string raw, int line)
    {
        var offset = 0;
        for (var i = 0; i < line; i++)
            offset += raw.Split('\n')[i].Length + 1;
        return offset;
    }

    [Fact]
    public void Preprocessing_really_does_shift_spans_but_not_lines()
    {
        // Sentinela del MECANISMO: si esto alguna vez deja de ser verdad (porque se sacó el
        // preprocesado, o porque se parsea el texto crudo), los tests de abajo pierden sentido
        // y este falla primero para avisarlo.
        var svc = NewMarkdownService();
        var doc = svc.ParsePreviewAst(CrashShape);
        var paragraph = Assert.Single(doc.Descendants<ParagraphBlock>());

        // El span apunta MÁS ALLÁ del final del texto crudo: ése era exactamente el
        // ArgumentOutOfRangeException ("0 <= offset <= 45 … Actual value was 65").
        Assert.True(paragraph.Span.End + 1 > CrashShape.Length,
            "el span debería excederse del texto crudo; si no, el preprocesado cambió");

        // La línea, en cambio, sigue siendo válida contra el crudo.
        Assert.Equal(0, paragraph.Line);
        Assert.Equal(2, CrashShape.Split('\n').Length);
    }

    [Fact]
    public void Find_block_resolves_to_a_line_valid_against_the_RAW_text_after_a_wikilink()
    {
        var svc = NewMarkdownService();
        // Primera línea con wikilink, párrafo destino DESPUÉS — la forma que rompía.
        const string markdown = "[[audio parte 001#^8jlekw]]pagina 1\n\ndestino ^a1b2c3";
        var doc = svc.ParsePreviewAst(markdown);

        var line = AnchorLocator.Find(doc, new LinkTarget("Nota", null, "a1b2c3", AnchorKind.Block));

        Assert.Equal(2, line);
        // Contra el texto CRUDO: la línea resuelta es de verdad la del párrafo marcado.
        Assert.Equal("destino ^a1b2c3", markdown.Split('\n')[line!.Value]);

        // Y el viejo Span.Start apuntaba a otro lado del crudo: la regresión que se corrigió.
        var marked = doc.Descendants<ParagraphBlock>()
            .Single(p => p.TryGetAttributes()?.Id == BlockAnchorExtension.IdPrefix + "a1b2c3");
        Assert.NotEqual(RawOffsetOfLine(markdown, 2), marked.Span.Start);
    }

    [Fact]
    public void Find_heading_resolves_to_a_line_valid_against_the_RAW_text_after_a_wikilink()
    {
        var svc = NewMarkdownService();
        const string markdown = "[[audio parte 001#^8jlekw]]pagina 1\n\n## Conclusiones\n\ntexto";
        var doc = svc.ParsePreviewAst(markdown);

        var line = AnchorLocator.Find(doc, new LinkTarget("Nota", null, "Conclusiones", AnchorKind.Heading));

        Assert.Equal(2, line);
        Assert.Equal("## Conclusiones", markdown.Split('\n')[line!.Value]);

        var heading = Assert.Single(doc.Descendants<HeadingBlock>());
        Assert.NotEqual(RawOffsetOfLine(markdown, 2), heading.Span.Start);
    }

    [Fact]
    public void FindParagraphAtLine_picks_the_paragraph_under_the_click_not_a_span_shifted_one()
    {
        var svc = NewMarkdownService();
        const string markdown = "[[audio parte 001#^8jlekw]]primero\n\nsegundo";
        var doc = svc.ParsePreviewAst(markdown);

        var picked = AnchorLocator.FindParagraphAtLine(doc, line: 2);

        Assert.NotNull(picked);
        Assert.Equal(2, picked!.Line);

        // La contracara: el offset crudo de "segundo" CAE DENTRO del span del PRIMER párrafo,
        // así que la comparación vieja (offset vivo contra Span) elegía el párrafo equivocado
        // sin tirar ninguna excepción.
        var first = doc.Descendants<ParagraphBlock>().First();
        var rawOffsetOfSegundo = RawOffsetOfLine(markdown, 2);
        Assert.InRange(rawOffsetOfSegundo, first.Span.Start, first.Span.End);
    }

    // ─── Posición de inserción del marcador (el crash) ─────────────────────────────────

    /// <summary>
    /// Reproduce lo que hace <c>EditorView.CopyParagraphLink</c> contra un
    /// <see cref="TextDocument"/> de AvalonEdit REAL: traduce el offset del clic a línea,
    /// ubica el párrafo por línea e inserta al final de su última línea VIVA.
    /// </summary>
    private static string InsertMarkerLikeTheEditorDoes(
        MarkdownService svc, string rawText, int clickOffset, string markerId)
    {
        var live = new TextDocument(rawText);
        var ast  = svc.ParsePreviewAst(rawText);

        var line  = live.GetLineByOffset(clickOffset).LineNumber - 1;
        var block = AnchorLocator.FindParagraphAtLine(ast, line);
        Assert.NotNull(block);

        var lastLine = AnchorLocator.ParagraphLastLine(
            ast, block!, live.LineCount,
            n => live.GetText(live.GetLineByNumber(n + 1)));

        live.Insert(live.GetLineByNumber(lastLine + 1).EndOffset, " ^" + markerId);
        return live.Text;
    }

    [Fact]
    public void Marker_insert_does_not_throw_on_the_exact_document_that_crashed()
    {
        var svc = NewMarkdownService();

        // Las dos líneas son UN solo párrafo (no hay línea en blanco), así que el marcador va
        // al final de "pagina 2". Antes se calculaba Span.End + 1 = 65 sobre un documento de
        // 44 caracteres → ArgumentOutOfRangeException.
        var result = InsertMarkerLikeTheEditorDoes(svc, CrashShape, clickOffset: 0, "a1b2c3");

        Assert.Equal("[[audio parte 001#^8jlekw]]pagina 1\npagina 2 ^a1b2c3", result);
    }

    [Fact]
    public void Marker_insert_lands_in_the_right_paragraph_when_the_note_is_long_enough_not_to_throw()
    {
        var svc = NewMarkdownService();
        // Nota lo bastante larga como para que Span.End + 1 siga siendo un offset VÁLIDO: el
        // caso peor, porque el marcador entraba en silencio en el párrafo equivocado.
        const string markdown =
            "[[audio parte 001#^8jlekw]]primero\n\nsegundo\n\ntercero\n\ncuarto\n\nquinto\n\nsexto";

        // Clic en cualquier punto del párrafo "segundo".
        var clickOffset = RawOffsetOfLine(markdown, 2) + 3;
        var result = InsertMarkerLikeTheEditorDoes(svc, markdown, clickOffset, "a1b2c3");

        Assert.Equal(
            "[[audio parte 001#^8jlekw]]primero\n\nsegundo ^a1b2c3\n\ntercero\n\ncuarto\n\nquinto\n\nsexto",
            result);
    }

    [Fact]
    public void Marker_insert_stops_at_the_heading_that_interrupts_the_paragraph()
    {
        var svc = NewMarkdownService();
        // Un heading corta el párrafo SIN línea en blanco de por medio: el marcador no puede
        // irse a la línea del heading.
        const string markdown = "[[Nota#^8jlekw]]texto\n# Titulo\n\notro";

        var result = InsertMarkerLikeTheEditorDoes(svc, markdown, clickOffset: 0, "a1b2c3");

        Assert.Equal("[[Nota#^8jlekw]]texto ^a1b2c3\n# Titulo\n\notro", result);
    }
}
