using System.Linq;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MarkdownVault.Services;

/// <summary>
/// Pure recognition/stripping logic for a trailing <c>^id</c> block marker — the literal LAST
/// top-level inline of a paragraph. Kept independent of Markdig's parser/event plumbing so it
/// is unit-testable (Phase 8) with plain strings, no pipeline required.
/// </summary>
public static class BlockMarker
{
    // `^` + first alnum char + 5-63 more alnum/hyphen chars (6-64 total after the caret).
    // Matches Obsidian's own generated shape (6 lowercase base-36 chars, see MarkerId) with
    // headroom for a longer hand-authored Obsidian id. The `(?<=\s)` lookbehind requires an
    // actual whitespace character immediately before the `^` WITHIN this same literal run —
    // if the marker sits at position 0 the lookbehind has nothing to look at and fails, which
    // is exactly "not preceded by whitespace" (proposal decision #1 / spec: "Block Marker
    // Recognition"). It also means `x^2` and `ver anexo^3` are rejected on shape length alone.
    private static readonly Regex MarkerAtEnd = new(
        @"(?<=\s)\^([A-Za-z0-9][A-Za-z0-9-]{5,63})$",
        RegexOptions.Compiled);

    /// <summary>
    /// Attempts to strip a trailing block marker from <paramref name="literalText"/> — the raw
    /// text of a paragraph's LAST top-level inline (never a nested one; that structural
    /// restriction is what excludes <c>**bold ^id**</c> and a marker-shaped code span, see
    /// <see cref="BlockAnchorExtension"/>). Returns the text with the marker AND the whitespace
    /// immediately before it removed, plus the bare id (no <c>^</c>) — or the original text and
    /// a <c>null</c> id when nothing is recognized at the literal end.
    /// </summary>
    public static (string Text, string? Id) TryStrip(string literalText)
    {
        if (string.IsNullOrEmpty(literalText))
            return (literalText, null);

        var match = MarkerAtEnd.Match(literalText);
        if (!match.Success)
            return (literalText, null);

        var stripped = literalText[..match.Index].TrimEnd(' ', '\t');
        return (stripped, match.Groups[1].Value);
    }
}

/// <summary>
/// Host-owned Markdig extension (design decision #3): gives a paragraph ending in a recognized
/// <c>^id</c> block marker an addressable HTML id, stripping the marker from the rendered text.
/// Hooks <see cref="MarkdownPipelineBuilder.DocumentProcessed"/>, which fires once BOTH block
/// AND inline parsing have finished for the whole document (see the CORRECTED remark on
/// <see cref="ProcessDocument"/> for why this replaced the original per-paragraph
/// <c>BlockParser.Closed</c> hook at Phase 8). Registered before the plugin-contribution loop in
/// <see cref="MarkdownService.GetPipeline"/> (design decision #4, Q6): event handlers fire in
/// subscription order, so this handler strips the marker and stamps the id BEFORE any
/// plugin-contributed extension (e.g. Callouts, which restructures blocks at its own
/// <c>DocumentProcessed</c> time) gets a chance to touch the paragraph.
/// </summary>
/// <remarks>
/// AST-only: it never sees raw markdown text, only already-parsed inlines, so fenced/inline
/// code and Mermaid's <c>[[subroutine]]</c> shape are structurally immune — there is no regex
/// running over source text the way <c>CodeSpanRegex</c> exists to guard against for wikilinks.
/// Stateless: nothing is cached on the extension instance itself, so a fresh
/// <see cref="MarkdownPipelineBuilder"/> rebuild (<c>MarkdownService.GetPipeline</c> invalidates
/// <c>_pipeline</c> on plugin toggle) needs no reset between documents or between rebuilds.
/// </remarks>
public sealed class BlockAnchorExtension : IMarkdownExtension
{
    /// <summary>
    /// HTML id namespace for a block marker (design decision #2). The <c>.md</c> source keeps
    /// the bare <c>^a1b2c3</c> for Obsidian compatibility; the rendered <c>id</c> is namespaced
    /// so a raw <c>^</c> never has to survive a URL fragment unescaped, and so a block id can
    /// never collide with a heading's GitHub-slug id.
    /// </summary>
    public const string IdPrefix = "mv-b-";

    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        // NOT ParagraphBlockParser.Closed (see the remark on ProcessDocument) — that event
        // fires during the BLOCK-parsing phase, strictly BEFORE inline parsing runs, so
        // paragraph.Inline is still null there. DocumentProcessed fires once for the whole
        // document, after blocks AND inlines both exist.
        pipeline.DocumentProcessed += ProcessDocument;
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        // No render-time hook needed: the id already sits on the paragraph's HtmlAttributes by
        // parse time, and ParagraphRenderer.Write already emits WriteAttributes — proven by
        // UseAdvancedExtensions() bundling GenericAttributes (`{#id}` syntax), which relies on
        // the exact same rendering path for a plain paragraph.
    }

    /// <remarks>
    /// CORRECTED at Phase 8 (see apply-progress.md) — the ORIGINAL implementation (batches 1-2)
    /// hooked <c>ParagraphBlockParser.Closed</c>, mirroring <c>AutoIdentifierExtension</c>'s OWN
    /// <c>HeadingBlockParser_Closed</c> subscription BY NAME alone (confirmed only to exist via
    /// reflection at apply time, never confirmed to actually WORK end to end). But
    /// <c>AutoIdentifierExtension</c> does not compute anything inside that handler — it also
    /// defines a SEPARATE <c>HeadingBlock_ProcessInlinesEnd</c>, which is what actually stamps
    /// the id, run only once that block's own inline parsing has completed.
    /// <c>BlockParser.Closed</c> fires during the BLOCK-parsing phase, strictly BEFORE inline
    /// parsing runs at all — so <c>paragraph.Inline</c> was <c>null</c> every single time the
    /// old handler ran, and the ENTIRE block-marker feature was silently inert against the real
    /// pipeline. Batches 1-2's own verification method (a disposable scratch console app driving
    /// plain strings) could never have caught this, because it never exercised the actual event
    /// timing end to end. A Phase 8 xUnit test run against the REAL <see cref="MarkdownService"/>
    /// pipeline (not a synthetic pipeline) did. Fixed by subscribing to
    /// <see cref="MarkdownPipelineBuilder.DocumentProcessed"/> instead.
    ///
    /// Fixing the timing surfaced a SECOND, independent problem: even with
    /// <c>paragraph.Inline</c> populated, a single trailing <c>LiteralInline</c> is not what
    /// "some text ^id" actually parses to. <c>UseAdvancedExtensions()</c> enables EmphasisExtra's
    /// Superscript/Subscript delimiters (<c>^text^</c> / <c>~text~</c>), so the inline parser
    /// tokenizes a bare, UNMATCHED <c>^</c> into its own <c>LiteralInline</c> the moment it scans
    /// one — splitting <c>"texto ^a1b2c3"</c> into THREE sibling literals ("texto ", "^",
    /// "a1b2c3"), never one. <see cref="TryMarkParagraph"/> walks the whole trailing run of
    /// consecutive literals instead of just <c>LastChild</c> to cover this — see its own remark.
    /// </remarks>
    private static void ProcessDocument(MarkdownDocument document)
    {
        foreach (var paragraph in document.Descendants<ParagraphBlock>())
            TryMarkParagraph(paragraph);
    }

    /// <remarks>
    /// Walks the TRAILING run of consecutive <see cref="LiteralInline"/> siblings, in source
    /// order, instead of checking only <c>paragraph.Inline.LastChild</c> — see the second half of
    /// <see cref="ProcessDocument"/>'s remark for why a single literal is not a safe assumption
    /// once <c>UseAdvancedExtensions()</c> is in the pipeline. Stopping at the first NON-literal
    /// sibling preserves every existing structural exclusion exactly as designed: a marker
    /// sitting inside emphasis/a link (<c>**bold ^id**</c>) or a code span wraps the text in a
    /// different inline node type (<c>EmphasisInline</c>/<c>LinkInline</c>/<c>CodeInline</c>) —
    /// never a bare <c>LiteralInline</c> — so the walk simply never reaches past it. This only
    /// widens HOW MANY literal nodes are considered, never WHICH KIND of node can hold a marker.
    /// </remarks>
    private static void TryMarkParagraph(ParagraphBlock paragraph)
    {
        if (paragraph.Inline is null)
            return;

        var trailing = new List<LiteralInline>();
        for (var inline = paragraph.Inline.LastChild; inline is LiteralInline literal; inline = inline.PreviousSibling)
            trailing.Insert(0, literal);

        if (trailing.Count == 0)
            return;

        var combinedText = string.Concat(trailing.Select(l => l.Content.ToString()));
        var (strippedText, id) = BlockMarker.TryStrip(combinedText);
        if (id is null)
            return;

        ApplyStrip(trailing, strippedText);
        paragraph.GetAttributes().Id = IdPrefix + id;
    }

    /// <summary>
    /// Redistributes <paramref name="keepText"/> — the trailing run's own combined text, minus
    /// the marker <see cref="BlockMarker.TryStrip"/> recognized — back across
    /// <paramref name="trailing"/> in the same order: full nodes are kept untouched while their
    /// length still fits inside <paramref name="keepText"/>, the ONE node straddling the cut
    /// point is truncated (or removed outright, if nothing of it survives), and every node
    /// entirely inside the stripped marker is detached from the AST via <see cref="Inline.Remove"/>.
    /// </summary>
    private static void ApplyStrip(List<LiteralInline> trailing, string keepText)
    {
        var remaining = keepText.Length;
        for (var i = 0; i < trailing.Count; i++)
        {
            var literal = trailing[i];
            var len = literal.Content.Length;
            if (remaining >= len)
            {
                remaining -= len;
                continue;
            }

            if (remaining == 0)
                literal.Remove();
            else
                literal.Content = new StringSlice(literal.Content.ToString()[..remaining]);

            for (var j = trailing.Count - 1; j > i; j--)
                trailing[j].Remove();
            return;
        }
    }
}
