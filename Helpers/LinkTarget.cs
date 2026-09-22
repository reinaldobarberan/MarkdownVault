namespace MarkdownVault.Helpers;

/// <summary>Classifies the anchor half of a parsed <see cref="LinkTarget"/>.</summary>
public enum AnchorKind
{
    /// <summary>No <c>#</c> in the raw target.</summary>
    None,

    /// <summary>A heading anchor (<c>#Sección</c>) — matched by id first, then by heading text.</summary>
    Heading,

    /// <summary>A block anchor (<c>#^a1b2c3</c>) — matched by the namespaced paragraph id.</summary>
    Block,
}

/// <summary>
/// Pure parser for a wikilink/standard-link inner target — the SOLE splitter of <c>|alias</c>
/// and <c>#anchor</c> in the codebase (design decision #1). Every call site that used to hand
/// a raw, unsplit target to <c>FileService.ResolveInternalLink</c> fed the anchor text straight
/// into the filename, minting a junk file (e.g. <c>Nota#Sección.md</c>) — this type exists so
/// that mistake has exactly one place left to happen, and it doesn't.
/// </summary>
/// <param name="Note">
/// The path/note half, with no <c>|alias</c> and no <c>#anchor</c>. Empty means the raw target
/// started with <c>#</c> — an intra-document anchor, which MUST NOT reach file resolution.
/// </param>
/// <param name="Alias">The <c>|alias</c> display-text override, or <c>null</c> when absent.</param>
/// <param name="Anchor">
/// The anchor value with its sigil removed: heading text as written, or a block id with the
/// leading <c>^</c> stripped. <c>null</c> when <see cref="Kind"/> is <see cref="AnchorKind.None"/>.
/// </param>
/// <param name="Kind">What the anchor half means, or <see cref="AnchorKind.None"/> if absent.</param>
public readonly record struct LinkTarget(string Note, string? Alias, string? Anchor, AnchorKind Kind)
{
    /// <summary>
    /// Parses <paramref name="raw"/> — the inner text of <c>[[raw]]</c> or the target half of
    /// <c>[text](raw)</c> — into its note/alias/anchor parts. Never throws.
    /// </summary>
    /// <remarks>
    /// Split order matters and is NOT symmetric: the first unescaped <c>|</c> is found across
    /// the WHOLE raw string first, separating the alias off the end
    /// (<c>Nota#Sección|Alias</c> → alias half <c>Alias</c>). Only THEN is the first unescaped
    /// <c>#</c> looked for, and only within what's left of the string — so a <c>#</c> that
    /// happens to sit inside the alias text itself (<c>Nota|Foo#Bar</c> → alias <c>Foo#Bar</c>)
    /// is never mistaken for an anchor separator. Doing it in the other order would corrupt
    /// that case, because the alias would still contain the raw <c>#</c> after a naive
    /// pipe-first-then-hash-on-everything split.
    /// </remarks>
    public static LinkTarget Parse(string raw)
    {
        raw ??= string.Empty;

        var noteAndAnchor = raw;
        string? alias = null;

        var pipeIndex = IndexOfUnescaped(raw, '|');
        if (pipeIndex >= 0)
        {
            noteAndAnchor = raw[..pipeIndex];
            var aliasText = raw[(pipeIndex + 1)..].Trim();
            alias = aliasText.Length == 0 ? null : aliasText;
        }

        var note = noteAndAnchor;
        string? anchorRaw = null;

        var hashIndex = IndexOfUnescaped(noteAndAnchor, '#');
        if (hashIndex >= 0)
        {
            note = noteAndAnchor[..hashIndex];
            anchorRaw = noteAndAnchor[(hashIndex + 1)..].Trim();
        }
        note = note.Trim();

        if (string.IsNullOrEmpty(anchorRaw))
            return new LinkTarget(note, alias, null, AnchorKind.None);

        if (anchorRaw[0] == '^' && anchorRaw.Length > 1)
            return new LinkTarget(note, alias, anchorRaw[1..], AnchorKind.Block);

        return new LinkTarget(note, alias, anchorRaw, AnchorKind.Heading);
    }

    /// <summary>Index of the first occurrence of <paramref name="c"/> not preceded by a backslash.</summary>
    private static int IndexOfUnescaped(string s, char c)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == c) return i;
        }
        return -1;
    }
}
