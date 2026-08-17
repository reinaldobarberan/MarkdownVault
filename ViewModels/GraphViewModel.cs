using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkdownVault.Models;
using MarkdownVault.Services;

namespace MarkdownVault.ViewModels;

/// <summary>
/// Backing state for the Obsidian-style graph view: the node/link model, the live
/// filter/force parameters, and the currently active file. Owns no rendering — the
/// <c>GraphView</c> reads <see cref="Nodes"/>/<see cref="Links"/> and runs the
/// force simulation against them.
/// </summary>
public partial class GraphViewModel : ObservableObject
{
    private readonly GraphService _graphService;

    public GraphViewModel(GraphService graphService) => _graphService = graphService;

    /// <summary>The vault root currently graphed, or <c>null</c> before the first build /
    /// when the graph was last built with no owning root. Lets <see cref="BuildIfRootChangedAsync"/>
    /// skip a rebuild when focus moves between tabs of the SAME vault.</summary>
    private string? _builtRoot;

    // ─── Graph model ─────────────────────────────────────────────────────────

    public IReadOnlyList<GraphNode> Nodes { get; private set; } = [];
    public IReadOnlyList<GraphLink> Links { get; private set; } = [];

    public int NoteCount => Nodes.Count;
    public int LinkCount => Links.Count;

    /// <summary>Raised after the graph model is rebuilt so the view can reset its camera/hover.</summary>
    public event Action? GraphRebuilt;

    /// <summary>Raised when a node is clicked; carries the file's absolute path.</summary>
    public event Action<string>? FileOpenRequested;

    // ─── Filter / display state ──────────────────────────────────────────────

    [ObservableProperty] private string _search      = string.Empty;
    [ObservableProperty] private bool   _localGraph;
    [ObservableProperty] private bool   _showLabels  = true;

    /// <summary>Vault-relative path of the active file (highlighted node), or empty.</summary>
    [ObservableProperty] private string _activeFile  = string.Empty;

    // ─── Force coefficients (0.2–2.5, default 1.0) ───────────────────────────

    [ObservableProperty] private double _forceCenter = 1.0;
    [ObservableProperty] private double _forceRepel  = 1.0;
    [ObservableProperty] private double _forceLink   = 1.0;

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
        ApplyLocalFilter();
        OnPropertyChanged(nameof(NoteCount));
        OnPropertyChanged(nameof(LinkCount));
        GraphRebuilt?.Invoke();
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

    partial void OnSearchChanged(string value)     => ApplyLocalFilter();
    partial void OnLocalGraphChanged(bool value)   => ApplyLocalFilter();
    partial void OnActiveFileChanged(string value) => ApplyLocalFilter();

    /// <summary>
    /// Recomputes each node's visibility from the search text and local-graph toggle.
    /// Local mode keeps the active file and its direct neighbours only.
    /// </summary>
    public void ApplyLocalFilter()
    {
        var q = Search.Trim();

        HashSet<string>? allowed = null;
        if (LocalGraph && !string.IsNullOrEmpty(ActiveFile))
        {
            allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ActiveFile };
            foreach (var l in Links)
            {
                if (string.Equals(l.Source.Id, ActiveFile, StringComparison.OrdinalIgnoreCase)) allowed.Add(l.Target.Id);
                if (string.Equals(l.Target.Id, ActiveFile, StringComparison.OrdinalIgnoreCase)) allowed.Add(l.Source.Id);
            }
        }

        foreach (var node in Nodes)
        {
            bool vis = allowed is null || allowed.Contains(node.Id);
            if (vis && q.Length > 0)
                vis = node.Label.Contains(q, StringComparison.OrdinalIgnoreCase);
            node.Visible = vis;
        }
    }

    // ─── Commands ────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task Refresh() => await BuildAsync(_builtRoot);
}
