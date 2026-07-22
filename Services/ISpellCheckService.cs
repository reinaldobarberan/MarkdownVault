namespace MarkdownVault.Services;

/// <summary>
/// A single spelling error: a span of misspelled text. Offsets are relative to
/// whatever text was passed to <see cref="ISpellCheckService.Check"/> — the caller
/// is responsible for translating them into absolute document offsets.
/// </summary>
public readonly record struct SpellError(int Offset, int Length);

/// <summary>
/// Abstraction over a spell-checking engine. The default implementation
/// (<see cref="WindowsSpellCheckService"/>) wraps the Windows <c>ISpellChecker</c>
/// COM API and uses the operating-system dictionaries for the current UI culture.
/// Kept as an interface so the rendering layer never depends on the concrete engine
/// and a suggestions/context-menu layer can be added later without touching it.
/// </summary>
public interface ISpellCheckService
{
    /// <summary>
    /// True when a dictionary for the current (or a fallback) language was loaded.
    /// When false, <see cref="Check"/> returns an empty list and the editor shows
    /// no underlines — spell checking is silently disabled rather than crashing.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>The BCP-47 language tag actually in use (e.g. <c>es-ES</c>), or empty when unavailable.</summary>
    string LanguageTag { get; }

    /// <summary>
    /// Returns the misspelled spans found in <paramref name="text"/>. Offsets are
    /// relative to <paramref name="text"/>. Never throws — returns an empty list on failure.
    /// </summary>
    IReadOnlyList<SpellError> Check(string text);

    /// <summary>
    /// Returns replacement suggestions for a single (presumed misspelled) word,
    /// best match first. Empty when unavailable or no suggestions exist.
    /// Not consumed by the v1 renderer, but exposed for a future context menu.
    /// </summary>
    IReadOnlyList<string> Suggest(string word);
}
