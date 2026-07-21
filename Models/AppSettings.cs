namespace MarkdownVault.Models;

/// <summary>Persisted user preferences loaded from / saved to AppData.</summary>
public class AppSettings
{
    public string LastVaultPath       { get; set; } = string.Empty;
    public bool   IsDarkTheme         { get; set; } = false;
    public string FontFamily          { get; set; } = "Consolas";
    public double FontSize            { get; set; } = 14;
    public bool     AutoSaveEnabled     { get; set; } = true;
    public int      AutoSaveIntervalSec { get; set; } = 30;
    public ViewMode ViewMode            { get; set; } = ViewMode.EditAndPreview;
    public bool   IsExplorerVisible   { get; set; } = true;
    public double FileTreeWidth       { get; set; } = 240;
    public double EditorColumnWidth   { get; set; } = 0;
    public double PreviewZoom         { get; set; } = 1.0;
}
