using System.IO;

namespace MarkdownVault.Models;

/// <summary>
/// Single source of truth for which file extensions MarkdownVault understands.
///
/// Two distinct concepts — do NOT collapse them:
///   • <see cref="Note"/>     — preview-native files that participate in the graph,
///                              wikilinks and the internal-link picker (a <c>[[link]]</c>
///                              target is always one of these).
///   • <see cref="CodeLanguage"/> — source-code files that are viewable/editable in the
///                              tree and editor, previewed as a highlighted code block,
///                              but are NEVER graph nodes or wikilink targets.
///   • <see cref="Viewable"/> — the union: everything the file tree lists and the editor opens.
/// </summary>
public static class SupportedExtensions
{
    /// <summary>Preview-native note formats (markdown, mermaid, raw HTML).</summary>
    public static readonly IReadOnlySet<string> Note =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".md", ".mermaid", ".mmd", ".html", ".htm" };

    /// <summary>
    /// Source-code extensions mapped to their highlight.js language id. The map both
    /// gates what the tree shows and tells the preview which fence language to wrap the
    /// file in, so the existing syntax-highlight plugin colours it.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> CodeLanguage =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"]   = "csharp",
            [".js"]   = "javascript",
            [".ts"]   = "typescript",
            [".py"]   = "python",
            [".json"] = "json",
            [".css"]  = "css",
            [".java"] = "java",
            [".go"]   = "go",
            [".rs"]   = "rust",
            [".cpp"]  = "cpp",
            [".c"]    = "c",
            [".sql"]  = "sql",
            [".sh"]   = "bash",
            [".xml"]  = "xml",
            [".yml"]  = "yaml",
            [".yaml"] = "yaml",
        };

    /// <summary>Every extension the tree lists and the editor can open (notes ∪ code).</summary>
    public static readonly IReadOnlySet<string> Viewable =
        new HashSet<string>(Note.Concat(CodeLanguage.Keys), StringComparer.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="path"/> is a preview-native note format.</summary>
    public static bool IsNote(string path) => Note.Contains(Path.GetExtension(path));

    /// <summary>True when <paramref name="path"/> is a supported source-code file.</summary>
    public static bool IsCode(string path) => CodeLanguage.ContainsKey(Path.GetExtension(path));

    /// <summary>True when <paramref name="path"/> can be listed in the tree and opened.</summary>
    public static bool IsViewable(string path) => Viewable.Contains(Path.GetExtension(path));

    /// <summary>highlight.js language id for a code file, or <c>null</c> if not a code file.</summary>
    public static string? LanguageFor(string path) =>
        CodeLanguage.TryGetValue(Path.GetExtension(path), out var lang) ? lang : null;
}
