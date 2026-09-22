using Markdig.Renderers.Html;
using Markdig.Syntax;
using MarkdownVault.Services;
using MarkdownVault.Services.Plugins;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Covers <see cref="BlockMarker.TryStrip"/> (pure, over a single inline literal's text) and,
/// for the shapes that TryStrip alone cannot see — a marker wrapped in emphasis/a code span, or
/// sitting inside a fenced code block — <see cref="BlockAnchorExtension"/>'s STRUCTURAL exclusion,
/// exercised through the real Markdig pipeline via <see cref="MarkdownService"/> (same convention
/// as <see cref="AnchorLocatorTests"/>). Traced to spec.md's "Block Marker Recognition" (R7) and
/// "Marker Invisibility With Addressable Anchor" (R8) requirements.
/// </summary>
public class BlockMarkerTests
{
    // ─── BlockMarker.TryStrip — pure, string in / (text, id?) out ───────────────────────────

    [Fact]
    public void TryStrip_recognizes_a_marker_at_the_end_preceded_by_whitespace()
    {
        var (text, id) = BlockMarker.TryStrip("texto final ^a1b2c3");

        Assert.Equal("texto final", text);
        Assert.Equal("a1b2c3", id);
    }

    [Fact]
    public void TryStrip_accepts_hyphens_after_the_first_character()
    {
        var (text, id) = BlockMarker.TryStrip("nota ^a1-b2-c3");

        Assert.Equal("nota", text);
        Assert.Equal("a1-b2-c3", id);
    }

    [Fact]
    public void TryStrip_rejects_a_caret_not_preceded_by_whitespace_x_caret_2()
    {
        // "x^2" — the design's own example of a rejected shape: no whitespace immediately
        // before '^' (the caret sits mid-word), so the lookbehind never matches.
        var (text, id) = BlockMarker.TryStrip("x^2");

        Assert.Null(id);
        Assert.Equal("x^2", text); // unchanged — nothing was recognized to strip
    }

    [Fact]
    public void TryStrip_rejects_ver_anexo_caret_3_no_whitespace_before_caret()
    {
        // The design's other named example: "ver anexo^3" — same reason as x^2, the caret is
        // glued to "anexo", not whitespace-preceded.
        var (text, id) = BlockMarker.TryStrip("ver anexo^3");

        Assert.Null(id);
        Assert.Equal("ver anexo^3", text);
    }

    [Fact]
    public void TryStrip_rejects_a_whitespace_preceded_caret_that_is_too_short()
    {
        // Whitespace-preceded this time, but only 1 char after '^' — the shape needs 6-64.
        // Isolates the LENGTH boundary from the whitespace boundary covered above.
        var (text, id) = BlockMarker.TryStrip("ver anexo ^3");

        Assert.Null(id);
        Assert.Equal("ver anexo ^3", text);
    }

    [Fact]
    public void TryStrip_returns_original_text_and_null_id_when_there_is_no_marker_at_all()
    {
        var (text, id) = BlockMarker.TryStrip("texto plano sin marcador");

        Assert.Null(id);
        Assert.Equal("texto plano sin marcador", text);
    }

    [Fact]
    public void TryStrip_handles_empty_string()
    {
        var (text, id) = BlockMarker.TryStrip("");

        Assert.Null(id);
        Assert.Equal("", text);
    }

    // ─── Structural exclusion (BlockAnchorExtension, real pipeline) ─────────────────────────

    private static MarkdownService NewMarkdownService() => new(new PluginRegistry());

    [Fact]
    public void Marker_inside_bold_emphasis_is_not_recognized()
    {
        var svc = NewMarkdownService();
        var doc = svc.ParsePreviewAst("**texto en negrita ^a1b2c3**");

        // LastChild of the paragraph's inline is an EmphasisInline, never a bare LiteralInline —
        // BlockAnchorExtension's structural check excludes it before BlockMarker.TryStrip ever runs.
        var paragraph = Assert.Single(doc.Descendants<ParagraphBlock>());
        Assert.Null(paragraph.TryGetAttributes()?.Id);
        Assert.Empty(svc.GetBlockMarkers("**texto en negrita ^a1b2c3**"));
    }

    [Fact]
    public void Marker_inside_an_inline_code_span_is_not_recognized()
    {
        var svc = NewMarkdownService();
        const string markdown = "texto con `^a1b2c3` en código";
        var doc = svc.ParsePreviewAst(markdown);

        var paragraph = Assert.Single(doc.Descendants<ParagraphBlock>());
        Assert.Null(paragraph.TryGetAttributes()?.Id);
        Assert.Empty(svc.GetBlockMarkers(markdown));
    }

    [Fact]
    public void Marker_shaped_token_inside_a_fenced_code_block_is_not_recognized()
    {
        // A fenced code block never produces a ParagraphBlock at all, so BlockAnchorExtension
        // (hooked to the paragraph parser's Closed event) never sees it — the whole block is
        // structurally immune, not merely a rejected shape.
        var svc = NewMarkdownService();
        const string markdown = "```\nvar x = 1; // ^a1b2c3\n```";

        Assert.Empty(svc.GetBlockMarkers(markdown));
    }

    [Fact]
    public void Recognized_marker_is_stripped_from_rendered_text_but_block_stays_addressable()
    {
        // spec.md "Marker Invisibility With Addressable Anchor": hidden in the rendered preview,
        // but the block still carries the id and is reachable via Nota#^id.
        var svc = NewMarkdownService();
        const string markdown = "Un párrafo marcado ^a1b2c3";

        var html = svc.RenderBody(markdown);
        var markers = svc.GetBlockMarkers(markdown);

        Assert.DoesNotContain("^a1b2c3", html);
        Assert.Contains($"id=\"{BlockAnchorExtension.IdPrefix}a1b2c3\"", html);
        Assert.Single(markers, m => m.Id == "a1b2c3");
    }
}
