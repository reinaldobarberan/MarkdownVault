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

    /// <summary>Rebuilds the graph from the vault and re-applies the current filter.</summary>
    public async Task BuildAsync()
    {
        var data = await _graphService.BuildAsync();
        Nodes = data.Nodes;
        Links = data.Links;
        ApplyLocalFilter();
        OnPropertyChanged(nameof(NoteCount));
        OnPropertyChanged(nameof(LinkCount));
        GraphRebuilt?.Invoke();
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
    private async Task Refresh() => await BuildAsync();
}
