using MarkdownVault.Helpers;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Covers <see cref="LinkTarget.Parse"/> — the SOLE splitter of <c>|alias</c> and <c>#anchor</c>
/// in the codebase (design decision #1, link-anchors change), traced to spec.md's "Link Target
/// Parsing" (R2) requirement and its scenarios. Pure string-in/record-out, no Markdig, no WPF —
/// matches the project's convention of testing pure helpers headlessly (see
/// <see cref="TextSearchTests"/>, <see cref="GraphFilterTests"/>).
/// </summary>
/// <remarks>
/// spec.md's own worked example ("<c>Nota#Sección|Alias</c> → path Nota, anchor Sección, alias
/// Alias") swaps the anchor/alias order relative to design.md decision #1, tasks.md 2.1, and
/// real Obsidian syntax (<c>[[Note#Heading|Display]]</c> — anchor BEFORE alias). Batch 1 flagged
/// this in apply-progress.md as an unresolved spec.md typo. These tests assert the
/// design.md/tasks.md/Obsidian-correct order, per that flag — NOT spec.md's literal string.
/// </remarks>
public class LinkTargetTests
{
    [Fact]
    public void Parse_bare_note_has_no_alias_or_anchor()
    {
        var t = LinkTarget.Parse("Nota");

        Assert.Equal("Nota", t.Note);
        Assert.Null(t.Alias);
        Assert.Null(t.Anchor);
        Assert.Equal(AnchorKind.None, t.Kind);
    }

    [Fact]
    public void Parse_heading_anchor_on_another_note()
    {
        var t = LinkTarget.Parse("Nota#Instalación");

        Assert.Equal("Nota", t.Note);
        Assert.Equal("Instalación", t.Anchor);
        Assert.Equal(AnchorKind.Heading, t.Kind);
        Assert.Null(t.Alias);
    }

    [Fact]
    public void Parse_block_anchor_strips_the_caret_sigil()
    {
        var t = LinkTarget.Parse("Nota#^a1b2c3");

        Assert.Equal("Nota", t.Note);
        Assert.Equal("a1b2c3", t.Anchor); // sigil removed, per the record's own doc comment
        Assert.Equal(AnchorKind.Block, t.Kind);
    }

    [Fact]
    public void Parse_intra_document_anchor_has_an_empty_note()
    {
        var t = LinkTarget.Parse("#Conclusiones");

        Assert.Equal(string.Empty, t.Note);
        Assert.Equal("Conclusiones", t.Anchor);
        Assert.Equal(AnchorKind.Heading, t.Kind);
    }

    [Fact]
    public void Parse_intra_document_block_anchor_has_an_empty_note()
    {
        var t = LinkTarget.Parse("#^a1b2c3");

        Assert.Equal(string.Empty, t.Note);
        Assert.Equal("a1b2c3", t.Anchor);
        Assert.Equal(AnchorKind.Block, t.Kind);
    }

    [Fact]
    public void Parse_alias_only_no_anchor()
    {
        var t = LinkTarget.Parse("Nota|Alias");

        Assert.Equal("Nota", t.Note);
        Assert.Equal("Alias", t.Alias);
        Assert.Null(t.Anchor);
        Assert.Equal(AnchorKind.None, t.Kind);
    }

    [Fact]
    public void Parse_heading_anchor_then_alias_the_real_Obsidian_order()
    {
        // The Obsidian-correct order: [[Note#Heading|Display]] — anchor BEFORE alias.
        var t = LinkTarget.Parse("Nota#H|Alias");

        Assert.Equal("Nota", t.Note);
        Assert.Equal("H", t.Anchor);
        Assert.Equal(AnchorKind.Heading, t.Kind);
        Assert.Equal("Alias", t.Alias);
    }

    [Fact]
    public void Parse_block_anchor_then_alias()
    {
        var t = LinkTarget.Parse("Nota#^id123|Ver aquí");

        Assert.Equal("Nota", t.Note);
        Assert.Equal("id123", t.Anchor);
        Assert.Equal(AnchorKind.Block, t.Kind);
        Assert.Equal("Ver aquí", t.Alias);
    }

    [Fact]
    public void Parse_empty_string_is_all_defaults()
    {
        var t = LinkTarget.Parse("");

        Assert.Equal(string.Empty, t.Note);
        Assert.Null(t.Alias);
        Assert.Null(t.Anchor);
        Assert.Equal(AnchorKind.None, t.Kind);
    }

    [Fact]
    public void Parse_a_hash_inside_the_alias_is_not_mistaken_for_an_anchor_separator()
    {
        // The pipe is split FIRST, across the WHOLE raw string — so a '#' that lands inside the
        // alias half is never re-split. Splitting hash-first-then-pipe would corrupt this case.
        var t = LinkTarget.Parse("Nota|Foo#Bar");

        Assert.Equal("Nota", t.Note);
        Assert.Equal("Foo#Bar", t.Alias);
        Assert.Null(t.Anchor);
        Assert.Equal(AnchorKind.None, t.Kind);
    }

    [Fact]
    public void Parse_trims_whitespace_around_note_and_alias()
    {
        var t = LinkTarget.Parse("  Nota  |  Alias con espacios  ");

        Assert.Equal("Nota", t.Note);
        Assert.Equal("Alias con espacios", t.Alias);
    }

    [Fact]
    public void Parse_a_lone_caret_with_nothing_after_it_is_not_a_block_anchor()
    {
        // anchorRaw == "^" (length 1) fails the "length > 1" guard, so it falls through to
        // Heading with the literal caret as anchor text — a known, harmless edge of the current
        // shape check, locked here as a regression guard.
        var t = LinkTarget.Parse("Nota#^");

        Assert.Equal(AnchorKind.Heading, t.Kind);
        Assert.Equal("^", t.Anchor);
    }

    [Fact]
    public void Parse_an_escaped_hash_is_never_treated_as_an_anchor_separator()
    {
        var t = LinkTarget.Parse(@"Nota\#NoEsAncla");

        Assert.Equal(AnchorKind.None, t.Kind);
        Assert.Null(t.Anchor);
        Assert.Equal(@"Nota\#NoEsAncla", t.Note);
    }

    [Fact]
    public void Parse_an_escaped_pipe_is_never_treated_as_an_alias_separator()
    {
        var t = LinkTarget.Parse(@"Nota\|NoEsAlias");

        Assert.Null(t.Alias);
        Assert.Equal(@"Nota\|NoEsAlias", t.Note);
    }

    [Fact]
    public void Parse_empty_alias_after_pipe_is_null_not_empty_string()
    {
        var t = LinkTarget.Parse("Nota|");

        Assert.Null(t.Alias);
    }
}
