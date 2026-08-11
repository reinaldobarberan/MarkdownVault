namespace MarkdownVault.Models;

/// <summary>Persisted user preferences loaded from / saved to AppData.</summary>
public class AppSettings
{
    public string LastVaultPath       { get; set; } = string.Empty;

    // Every vault root the user has opened, in the order they were first added.
    // Powers the "Administrar vaults" form so the user can switch between several
    // independent vaults. LastVaultPath still drives which one reopens on startup.
    public List<string> KnownVaultPaths { get; set; } = new();
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
    // Split-editor (Phase 4): geometry persists, open documents do not (SE-3). Read in
    // MainViewModel's ctor, written in SaveSettings — both ends wired deliberately, per
    // design C8's warning that FileTreeWidth/EditorColumnWidth below are declared-and-dead.
    public bool   IsSplit             { get; set; } = false;
    public double SplitRatio          { get; set; } = 0.5;
    // Spell-check dictionary language. Empty = auto-detect from OS culture.
    // A two-letter code ("es") or a full BCP-47 tag ("es-ES") both work.
    // Useful when the OS UI language differs from the language you actually write in.
    public string SpellCheckLanguage  { get; set; } = string.Empty;

    // Enabled/disabled state per plugin id (e.g. "core.mermaid": true).
    // A plugin absent from this map defaults to ENABLED (first-party core plugins
    // ship on). The Plugins window writes here when the user toggles one.
    public Dictionary<string, bool> PluginsEnabled { get; set; } = new();
}
