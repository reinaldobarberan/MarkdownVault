using System.IO;

namespace MarkdownVault.Services;

/// <summary>
/// Pure helper for deriving the default file name shown in the export dialogs
/// (PNG / PDF). Extracted from the WebView2 export handlers so the naming rule is
/// unit-testable in isolation — the handlers themselves depend on WebView2 and can't
/// run headless.
/// </summary>
public static class ExportNaming
{
    /// <summary>Name used when no file is open (no active tab, or an empty path).</summary>
    public const string Fallback = "export";

    /// <summary>
    /// Returns the base file name (no extension) of the active tab's path, or
    /// <see cref="Fallback"/> when there is no meaningful path to derive one from.
    /// </summary>
    public static string DefaultFileName(string? activeTabFilePath)
    {
        if (string.IsNullOrWhiteSpace(activeTabFilePath))
            return Fallback;

        var name = Path.GetFileNameWithoutExtension(activeTabFilePath);
        return string.IsNullOrWhiteSpace(name) ? Fallback : name;
    }
}
