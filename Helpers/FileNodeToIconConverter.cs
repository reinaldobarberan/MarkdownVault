using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace MarkdownVault.Helpers;

/// <summary>
/// Picks a representative Segoe MDL2 Assets glyph for a file-tree node:
/// open / closed folders, and per-extension file icons (documents, images, code).
/// MultiBinding order: [0] IsDirectory (bool), [1] IsExpanded (bool), [2] Name (string).
/// </summary>
public sealed class FileNodeToIconConverter : IMultiValueConverter
{
    // Folder glyphs.
    private const string FolderClosed = ""; // Folder
    private const string FolderOpen   = ""; // OpenFolderHorizontal

    // File glyphs.
    private const string Document = ""; // Document — markdown / text / unknown
    private const string Photo    = ""; // Photo2   — images
    private const string Code     = ""; // Code     — data / markup

    /// <summary>Pure glyph-selection logic, exposed for unit testing.</summary>
    public static string GlyphFor(bool isDirectory, bool isExpanded, string? name)
    {
        if (isDirectory)
            return isExpanded ? FolderOpen : FolderClosed;

        var ext = Path.GetExtension(name ?? string.Empty).ToLowerInvariant();
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" or ".ico"
                => Photo,
            ".json" or ".xml" or ".yml" or ".yaml" or ".html" or ".htm" or ".css" or ".js" or ".ts" or ".csv"
                => Code,
            _   => Document,
        };
    }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        bool    isDirectory = values.Length > 0 && values[0] is true;
        bool    isExpanded  = values.Length > 1 && values[1] is true;
        string? name        = values.Length > 2 ? values[2] as string : null;
        return GlyphFor(isDirectory, isExpanded, name);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
