using MarkdownVault.Services;

namespace MarkdownVault.Helpers;

/// <summary>
/// A misspelled word located within a single line: its <paramref name="Offset"/> and
/// <paramref name="Length"/> index the ORIGINAL line text (masking preserves length, so
/// they map straight back), and <paramref name="Word"/> is the source substring.
/// </summary>
public readonly record struct MisspelledWord(int Offset, int Length, string Word);

/// <summary>
/// Locates the misspelled word sitting under a given column on a line — the pure logic
/// behind the editor's right-click suggestions menu.
/// </summary>
/// <remarks>
/// This deliberately mirrors <see cref="SpellCheckColorizer"/>: it masks the line with
/// <see cref="MarkdownProseMask"/> and asks <see cref="ISpellCheckService.Check"/> for the
/// error spans, then picks the one the column falls in. Sharing the exact pipeline
/// guarantees the menu only ever offers to correct a word that is actually underlined.
/// </remarks>
public static class SpellCheckWordResolver
{
    /// <summary>
    /// Returns the misspelled word at <paramref name="column"/> (a 0-based offset within
    /// <paramref name="lineText"/>), or <c>null</c> when the column is out of range, over
    /// a correctly spelled word, or inside a masked region (code, URLs, link targets).
    /// The column matches a span inclusively at both ends, so a right-click landing on the
    /// caret position just before or just after the word still resolves it.
    /// </summary>
    public static MisspelledWord? FindMisspelledWordAt(
        ISpellCheckService spell, string lineText, int column)
    {
        if (spell is null || !spell.IsAvailable) return null;
        if (string.IsNullOrEmpty(lineText) || column < 0 || column > lineText.Length)
            return null;

        string masked = MarkdownProseMask.Mask(lineText);

        foreach (var span in spell.Check(masked))
        {
            if (column >= span.Offset && column <= span.Offset + span.Length)
                return new MisspelledWord(
                    span.Offset, span.Length, lineText.Substring(span.Offset, span.Length));
        }

        return null;
    }
}
