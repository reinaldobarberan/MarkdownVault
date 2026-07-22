using System.Globalization;
using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace MarkdownVault.Services;

/// <summary>
/// Spell-check engine backed by the Windows <c>ISpellChecker</c> COM API
/// (the same one Edge and Office use). It relies entirely on the language
/// dictionaries installed in the operating system — nothing is bundled with
/// the app — and picks the dictionary that best matches the current UI culture.
/// </summary>
/// <remarks>
/// AvalonEdit is a custom-rendered control, so WPF's built-in
/// <c>SpellCheck.IsEnabled</c> (which only works on TextBox/RichTextBox) does
/// not apply. This service provides the missing "does this word exist" primitive;
/// the visual underline lives in <see cref="Helpers.SpellCheckRenderer"/>.
///
/// The COM factory is registered ThreadingModel=Both, so it is created and used
/// on the WPF UI (STA) thread. All calls are wrapped so a missing API (pre-Win8)
/// or an uninstalled language degrades to <see cref="IsAvailable"/> == false
/// instead of crashing the app.
/// </remarks>
public sealed class WindowsSpellCheckService : ISpellCheckService
{
    private readonly ISpellChecker? _checker;

    public bool   IsAvailable => _checker is not null;
    public string LanguageTag { get; } = string.Empty;

    /// <param name="preferredLanguage">
    /// Explicit dictionary language — a two-letter code ("es") or full tag ("es-ES").
    /// When null or empty, the language is auto-detected from the OS culture.
    /// </param>
    public WindowsSpellCheckService(string? preferredLanguage = null)
    {
        try
        {
            var factory = (ISpellCheckerFactory)new SpellCheckerFactory();
            var tag     = ResolveLanguageTag(factory, preferredLanguage);
            if (tag is not null)
            {
                _checker    = factory.CreateSpellChecker(tag);
                LanguageTag = tag;
            }
        }
        catch
        {
            // API unavailable (older Windows) or COM failure → spell check stays off.
            _checker = null;
        }
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    public IReadOnlyList<SpellError> Check(string text)
    {
        if (_checker is null || string.IsNullOrEmpty(text))
            return [];

        try
        {
            var errors = _checker.Check(text);
            var list   = new List<SpellError>();
            while (errors.Next(out var error) == 0 && error is not null)
            {
                list.Add(new SpellError((int)error.get_StartIndex(), (int)error.get_Length()));
                Marshal.ReleaseComObject(error);
            }
            Marshal.ReleaseComObject(errors);
            return list;
        }
        catch
        {
            return [];
        }
    }

    public IReadOnlyList<string> Suggest(string word)
    {
        if (_checker is null || string.IsNullOrWhiteSpace(word))
            return [];

        try
        {
            var suggestions = _checker.Suggest(word);
            return ReadAll(suggestions);
        }
        catch
        {
            return [];
        }
    }

    // ─── Language resolution ──────────────────────────────────────────────────

    /// <summary>
    /// Resolves the dictionary to load, in priority order:
    /// <list type="number">
    ///   <item>the explicit <paramref name="preferred"/> language (full tag or two-letter code),</item>
    ///   <item>the current UI culture (exact tag, then same two-letter language),</item>
    ///   <item><c>en-US</c> as a last resort.</item>
    /// </list>
    /// Returns <c>null</c> when nothing usable is installed.
    /// </summary>
    private static string? ResolveLanguageTag(ISpellCheckerFactory factory, string? preferred)
    {
        var supported = ReadAll(factory.get_SupportedLanguages());

        // 1. Explicit user preference (e.g. "es" or "es-ES") — decoupled from OS UI language.
        var fromPreference = MatchLanguage(factory, supported, preferred);
        if (fromPreference is not null)
            return fromPreference;

        // 2. OS UI culture.
        var culture = CultureInfo.CurrentUICulture;
        if (IsSupported(factory, culture.Name))
            return culture.Name;

        var byCultureLanguage = supported.FirstOrDefault(
            t => t.StartsWith(culture.TwoLetterISOLanguageName + "-", StringComparison.OrdinalIgnoreCase));
        if (byCultureLanguage is not null)
            return byCultureLanguage;

        // 3. Fallback.
        return supported.FirstOrDefault(
            t => t.Equals("en-US", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Matches a requested language against the installed dictionaries. Accepts a full
    /// tag (used verbatim when supported) or a two-letter code, which prefers the
    /// matching <c>{lang}-ES</c>-style regional dictionary before any other variant.
    /// </summary>
    private static string? MatchLanguage(ISpellCheckerFactory factory, List<string> supported, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return null;

        requested = requested.Trim();

        // Full tag (contains a region) → use as-is when supported.
        if (requested.Contains('-'))
            return IsSupported(factory, requested) ? requested : null;

        // Two-letter code → prefer a "main" variant, else the first regional match.
        var candidates = supported
            .Where(t => t.StartsWith(requested + "-", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0)
            return null;

        var preferredRegion = requested.ToLowerInvariant() + "-" + requested.ToUpperInvariant(); // e.g. es → es-ES
        return candidates.FirstOrDefault(
                   t => t.Equals(preferredRegion, StringComparison.OrdinalIgnoreCase))
               ?? candidates[0];
    }

    private static bool IsSupported(ISpellCheckerFactory factory, string tag)
    {
        try { return factory.IsSupported(tag); }
        catch { return false; }
    }

    // ─── COM helpers ──────────────────────────────────────────────────────────

    /// <summary>Drains an <c>IEnumString</c> (used for supported languages and suggestions).</summary>
    private static List<string> ReadAll(ComTypes.IEnumString enumerator)
    {
        var result = new List<string>();
        var buffer = new string[1];
        var fetched = Marshal.AllocCoTaskMem(IntPtr.Size);
        try
        {
            while (enumerator.Next(1, buffer, fetched) == 0 && Marshal.ReadInt32(fetched) == 1)
                result.Add(buffer[0]);
        }
        finally
        {
            Marshal.FreeCoTaskMem(fetched);
            Marshal.ReleaseComObject(enumerator);
        }
        return result;
    }

    // ─── COM interop declarations (Spellcheck.h) ──────────────────────────────
    // Method order MUST match the native vtable exactly. Only the methods needed
    // (up to the last one called) are declared; the rest are intentionally omitted.

    [ComImport, Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC")]
    private class SpellCheckerFactory { }

    [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellCheckerFactory
    {
        [return: MarshalAs(UnmanagedType.Interface)]
        ComTypes.IEnumString get_SupportedLanguages();

        [return: MarshalAs(UnmanagedType.Bool)]
        bool IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag);

        [return: MarshalAs(UnmanagedType.Interface)]
        ISpellChecker CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag);
    }

    [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellChecker
    {
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string get_LanguageTag();

        [return: MarshalAs(UnmanagedType.Interface)]
        IEnumSpellingError Check([MarshalAs(UnmanagedType.LPWStr)] string text);

        [return: MarshalAs(UnmanagedType.Interface)]
        ComTypes.IEnumString Suggest([MarshalAs(UnmanagedType.LPWStr)] string word);
    }

    [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumSpellingError
    {
        // Returns S_OK (0) with an error, or S_FALSE (1) at the end of the enumeration.
        [PreserveSig]
        int Next([MarshalAs(UnmanagedType.Interface)] out ISpellingError? value);
    }

    [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellingError
    {
        uint get_StartIndex();
        uint get_Length();
        // CORRECTIVE_ACTION get_CorrectiveAction()  — not needed for v1 underlining.
    }
}
