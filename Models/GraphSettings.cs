namespace MarkdownVault.Models;

/// <summary>
/// The graph view's knobs, remembered per vault.
///
/// Per vault and not globally on purpose: the right settings depend on what the vault looks like.
/// A dense forty-note wiki wants small nodes and a high link floor; a loose pile of a thousand
/// notes wants the opposite. One global setting would be wrong for every vault but one.
///
/// The defaults here are the calibrated ones, so a vault that was never configured — or whose file
/// is missing or corrupt — opens exactly as it does today.
/// </summary>
public sealed class GraphSettings
{
    /// <summary>Format version, so a later change can migrate instead of discarding.</summary>
    public int Version { get; set; } = 1;

    // ── Filters ──
    public int  MinDegree  { get; set; }
    public int  LocalDepth { get; set; } = 1;
    public bool LocalGraph { get; set; }

    // ── Display ──
    public bool   ShowLabels      { get; set; } = true;
    public bool   ClusterByFolder { get; set; } = true;
    public bool   ThreeD          { get; set; }
    public double NodeScale       { get; set; } = 1.0;

    // ── Forces ──
    public double ForceCenter { get; set; } = 1.0;
    public double ForceRepel  { get; set; } = 1.0;
    public double ForceLink   { get; set; } = 1.0;

    /// <summary>
    /// Folders switched off in the legend, stored by NAME rather than by index. Indices are
    /// handed out in folder order, so adding or deleting one folder shifts every index after it —
    /// and the next load would hide the wrong folder.
    /// </summary>
    public List<string> HiddenFolders { get; set; } = [];

    /// <summary>The search box is deliberately NOT saved: it is a momentary question, not a setting.</summary>
    public static GraphSettings Defaults() => new();
}
