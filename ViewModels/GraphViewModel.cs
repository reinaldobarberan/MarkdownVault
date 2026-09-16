using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkdownVault.Models;
using MarkdownVault.Services;

namespace MarkdownVault.ViewModels;

/// <summary>
/// Backing state for the Obsidian-style graph view: the node/link model, the live
/// filter/force parameters, and the currently active file. Owns no rendering — the
/// <c>GraphView</c> reads <see cref="Nodes"/>/<see cref="Links"/> and runs the
/// force simulation against them — and owns no filtering logic either: that lives in
/// <see cref="GraphFilter"/> so it can be tested without a canvas.
/// </summary>
public partial class GraphViewModel : ObservableObject
{
    private readonly GraphService _graphService;
    private readonly GraphSettingsService _settingsService;

    public GraphViewModel(GraphService graphService, GraphSettingsService? settingsService = null)
    {
        _graphService    = graphService;
        _settingsService = settingsService ?? new GraphSettingsService();
    }

    /// <summary>
    /// True while stored settings are being poured into the properties. Every setter below reacts
    /// by re-laying-out and by queueing a save; without this guard, restoring ten values would run
    /// the layout ten times and immediately write back what was just read.
    /// </summary>
    private bool _restoring;

    /// <summary>The vault root currently graphed, or <c>null</c> before the first build /
    /// when the graph was last built with no owning root. Lets <see cref="BuildIfRootChangedAsync"/>
    /// skip a rebuild when focus moves between tabs of the SAME vault.</summary>
    private string? _builtRoot;

    // ─── Graph model ─────────────────────────────────────────────────────────

    public IReadOnlyList<GraphNode> Nodes { get; private set; } = [];
    public IReadOnlyList<GraphLink> Links { get; private set; } = [];

    public int NoteCount => Nodes.Count;
    public int LinkCount => Links.Count;

    /// <summary>Notes currently passing the filter — what the legend reports next to the total.</summary>
    public int VisibleNoteCount { get; private set; }

    /// <summary>Links with BOTH ends visible: the edges actually drawn.</summary>
    public int VisibleLinkCount { get; private set; }

    /// <summary>True when the filters are hiding something, so the view can say "N de M".</summary>
    public bool IsFiltered => VisibleNoteCount != NoteCount;

    // ─── Groups (vault areas) ────────────────────────────────────────────────

    /// <summary>Legend rows, one per top-level folder, in stable palette order.</summary>
    public ObservableCollection<GraphGroupViewModel> Groups { get; } = [];

    /// <summary>
    /// Only worth showing the legend when the vault actually has more than one area — in a flat
    /// vault every note is the same colour and a one-row legend is just noise.
    /// </summary>
    public bool HasGroups => Groups.Count > 1;

    /// <summary>Pull each folder's notes to their own anchor instead of all to the centre.</summary>
    [ObservableProperty] private bool _clusterByFolder = true;

    /// <summary>
    /// Lay the vault out in three dimensions instead of on a plane. Flipping this re-runs the
    /// layout rather than just tilting the camera: a sphere of anchors flattened onto a plane
    /// would drop two folders on the same spot, so each mode needs its own set of anchors.
    /// </summary>
    [ObservableProperty] private bool _threeD;

    private readonly HashSet<int> _hiddenGroups = [];

    /// <summary>Raised after the graph model is rebuilt so the view can reset its camera/hover.</summary>
    public event Action? GraphRebuilt;

    /// <summary>Raised when a node is clicked; carries the file's absolute path.</summary>
    public event Action<string>? FileOpenRequested;

    // ─── Filter / display state ──────────────────────────────────────────────

    [ObservableProperty] private string _search      = string.Empty;
    [ObservableProperty] private bool   _localGraph;
    [ObservableProperty] private bool   _showLabels  = true;

    /// <summary>Hops out from the active note that local mode reaches (1 = direct neighbours only).</summary>
    [ObservableProperty] private int _localDepth = 1;

    /// <summary>Link floor: notes with fewer links are hidden. 1 already clears every orphan.</summary>
    [ObservableProperty] private int _minDegree;

    /// <summary>Vault-relative path of the active file (highlighted node), or empty.</summary>
    [ObservableProperty] private string _activeFile  = string.Empty;

    // ─── Force coefficients (0.2–2.5, default 1.0) ───────────────────────────

    [ObservableProperty] private double _forceCenter = 1.0;
    [ObservableProperty] private double _forceRepel  = 1.0;
    [ObservableProperty] private double _forceLink   = 1.0;

    // ─── Display ─────────────────────────────────────────────────────────────

    /// <summary>
    /// How big the nodes are drawn, 1.0 being the calibrated default. It re-stamps the radii
    /// rather than scaling at draw time, because the radius is also what keeps nodes from
    /// overlapping and what the camera measures when it frames the graph.
    /// </summary>
    [ObservableProperty] private double _nodeScale = 1.0;

    // ─── Build ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the graph scoped to <paramref name="root"/> and re-applies the current filter.
    /// Pass <c>null</c> when no tab is focused inside any open vault — the graph clears to
    /// empty rather than falling back to some other vault (vault-scoped-resolution spec: no
    /// cross-vault nodes/edges).
    /// </summary>
    public async Task BuildAsync(string? root)
    {
        _builtRoot = root;
        var data = root is not null ? await _graphService.BuildAsync(root) : new GraphData([], []);
        Nodes = data.Nodes;
        Links = data.Links;

        // The vault's remembered knobs go in BEFORE the layout runs: node size and the flat/3D
        // choice both decide where the anchors end up, so restoring them afterwards would lay the
        // graph out twice and show the wrong one first.
        var saved = root is not null ? _settingsService.Load(root) : GraphSettings.Defaults();
        Restore(saved);

        RebuildGroups();
        RestoreHiddenFolders(saved.HiddenFolders);
        ApplyLocalFilter();

        OnPropertyChanged(nameof(NoteCount));
        OnPropertyChanged(nameof(LinkCount));
        GraphRebuilt?.Invoke();
    }

    // ─── Per-vault settings ──────────────────────────────────────────────────

    /// <summary>Pours stored values into the properties without triggering a save or a relayout.</summary>
    private void Restore(GraphSettings s)
    {
        _restoring = true;
        try
        {
            MinDegree       = s.MinDegree;
            LocalDepth      = s.LocalDepth;
            LocalGraph      = s.LocalGraph;
            ShowLabels      = s.ShowLabels;
            ClusterByFolder = s.ClusterByFolder;
            ThreeD          = s.ThreeD;
            NodeScale       = s.NodeScale;
            ForceCenter     = s.ForceCenter;
            ForceRepel      = s.ForceRepel;
            ForceLink       = s.ForceLink;
        }
        finally
        {
            _restoring = false;
        }
    }

    /// <summary>
    /// Re-applies the folder switches AFTER the legend rows exist. Matching is by folder NAME:
    /// indices shift whenever a folder is added or removed, and by index this would quietly start
    /// hiding the wrong folder.
    /// </summary>
    private void RestoreHiddenFolders(List<string> hidden)
    {
        if (hidden.Count == 0) return;

        var off = new HashSet<string>(hidden, StringComparer.OrdinalIgnoreCase);

        _restoring = true;
        try
        {
            foreach (var row in Groups)
                if (off.Contains(row.Name)) row.IsVisible = false;
        }
        finally
        {
            _restoring = false;
        }
    }

    /// <summary>Current knob positions, ready to be written.</summary>
    private GraphSettings Capture() => new()
    {
        MinDegree       = MinDegree,
        LocalDepth      = LocalDepth,
        LocalGraph      = LocalGraph,
        ShowLabels      = ShowLabels,
        ClusterByFolder = ClusterByFolder,
        ThreeD          = ThreeD,
        NodeScale       = NodeScale,
        ForceCenter     = ForceCenter,
        ForceRepel      = ForceRepel,
        ForceLink       = ForceLink,
        HiddenFolders   = Groups.Where(g => !g.IsVisible).Select(g => g.Name).ToList()
    };

    /// <summary>
    /// Hands the current settings to the service, which debounces the actual write — a slider drag
    /// fires this dozens of times and only the last one is worth putting on disk.
    /// </summary>
    private void QueueSave()
    {
        if (_restoring || _builtRoot is null) return;
        _settingsService.Save(_builtRoot, Capture());
    }

    /// <summary>Writes any queued settings now. For app shutdown, where the debounce has no time to elapse.</summary>
    public void FlushSettings() => _settingsService.Flush();

    /// <summary>
    /// Re-derives the layout and the legend from the current nodes. All the real work happens in
    /// <see cref="GraphLayout"/> (folders, islands, layout targets, parked orphans); this only
    /// mirrors the resulting groups into rows the legend can bind to.
    ///
    /// Group indices are deterministic, so a folder keeps its colour across rebuilds. The on/off
    /// switches ARE reset, because the old indices may no longer mean the same folder once the
    /// vault changed underneath.
    /// </summary>
    private void RebuildGroups()
    {
        var groups = GraphLayout.Assign(Nodes, Links, ThreeD, NodeScale);

        foreach (var old in Groups) old.VisibilityChanged -= OnGroupVisibilityChanged;
        Groups.Clear();
        _hiddenGroups.Clear();

        foreach (var g in groups)
        {
            var row = new GraphGroupViewModel(g);
            row.VisibilityChanged += OnGroupVisibilityChanged;
            Groups.Add(row);
        }

        OnPropertyChanged(nameof(HasGroups));
    }

    private void OnGroupVisibilityChanged()
    {
        _hiddenGroups.Clear();
        foreach (var g in Groups)
            if (!g.IsVisible) _hiddenGroups.Add(g.Index);

        if (_restoring) return;
        ApplyLocalFilter();
        QueueSave();
    }

    /// <summary>
    /// Rebuilds only when <paramref name="root"/> differs from the vault currently graphed —
    /// called on every focus change (including plain tab switches within the same vault), but
    /// a no-op unless focus actually crossed into a different open vault (Phase 6 / D7: the
    /// graph follows the focused tab's vault, not every tab switch).
    /// </summary>
    public Task BuildIfRootChangedAsync(string? root)
    {
        if (root is not null && string.Equals(root, _builtRoot, StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;
        return BuildAsync(root);
    }

    /// <summary>Opens the file behind a node id (its vault-relative path).</summary>
    public void RequestOpen(string nodeId)
    {
        var node = Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is not null)
            FileOpenRequested?.Invoke(node.FullPath);
    }

    // ─── Filtering ───────────────────────────────────────────────────────────

    /// <summary>
    /// Switching between flat and spatial only moves the anchors — the folders, the islands and
    /// the legend are unchanged, so this re-runs the LAYOUT without rebuilding the group rows.
    /// Going through <see cref="RebuildGroups"/> would reset the user's folder switches, which has
    /// nothing to do with tilting the vault into three dimensions.
    /// </summary>
    partial void OnThreeDChanged(bool value)
    {
        if (_restoring) return;
        GraphLayout.Assign(Nodes, Links, value, NodeScale);
        QueueSave();
    }

    /// <summary>
    /// Resizing only needs the radii re-stamped — the folders, islands and anchors do not move —
    /// so this skips the full layout pass.
    /// </summary>
    partial void OnNodeScaleChanged(double value)
    {
        if (_restoring) return;
        GraphMetrics.Assign(Nodes, value);
        QueueSave();
    }

    // The search box is a momentary question, not a setting: it filters but is never saved.
    partial void OnSearchChanged(string value)     => ApplyLocalFilter();
    // Neither is the active file — it follows the focused tab.
    partial void OnActiveFileChanged(string value) => ApplyLocalFilter();

    partial void OnLocalGraphChanged(bool value)   => FilterChanged();
    partial void OnLocalDepthChanged(int value)    => FilterChanged();
    partial void OnMinDegreeChanged(int value)     => FilterChanged();
    partial void OnShowLabelsChanged(bool value)      => QueueSave();
    partial void OnClusterByFolderChanged(bool value) => QueueSave();
    partial void OnForceCenterChanged(double value)   => QueueSave();
    partial void OnForceRepelChanged(double value)    => QueueSave();
    partial void OnForceLinkChanged(double value)     => QueueSave();

    private void FilterChanged()
    {
        if (_restoring) return;
        ApplyLocalFilter();
        QueueSave();
    }

    /// <summary>
    /// Recomputes each node's visibility from the current filter state and refreshes the
    /// visible counters. The decision itself belongs to <see cref="GraphFilter"/>; this only
    /// stamps the answer onto the live nodes the renderer reads.
    /// </summary>
    public void ApplyLocalFilter()
    {
        var visible = GraphFilter.VisibleIds(Nodes, Links, new GraphFilterOptions(
            Search:     Search,
            LocalGraph: LocalGraph,
            ActiveFile: ActiveFile,
            LocalDepth:   LocalDepth,
            MinDegree:    MinDegree,
            HiddenGroups: _hiddenGroups.Count > 0 ? _hiddenGroups : null));

        foreach (var node in Nodes)
            node.Visible = visible.Contains(node.Id);

        VisibleNoteCount = visible.Count;

        int drawn = 0;
        foreach (var l in Links)
            if (l.Source.Visible && l.Target.Visible) drawn++;
        VisibleLinkCount = drawn;

        OnPropertyChanged(nameof(VisibleNoteCount));
        OnPropertyChanged(nameof(VisibleLinkCount));
        OnPropertyChanged(nameof(IsFiltered));
    }

    // ─── Commands ────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task Refresh() => await BuildAsync(_builtRoot);
}
