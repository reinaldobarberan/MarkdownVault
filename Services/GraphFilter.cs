using MarkdownVault.Models;

namespace MarkdownVault.Services;

/// <summary>
/// Everything the graph view can use to narrow what is on screen. A plain record so the
/// filter engine stays a pure function of its inputs (testable headless, no WPF).
/// </summary>
/// <param name="Search">Substring matched against a node's label (case-insensitive). Empty = no text filter.</param>
/// <param name="LocalGraph">Restrict to the neighbourhood of <paramref name="ActiveFile"/>.</param>
/// <param name="ActiveFile">Vault-relative id of the focused note. Ignored when empty.</param>
/// <param name="LocalDepth">How many hops out from the active file local mode reaches (1 = direct neighbours).</param>
/// <param name="MinDegree">Hide notes with fewer than this many links. 0 = show everything, 1 = hide orphans.</param>
/// <param name="HiddenGroups">Group indices switched off in the legend. <c>null</c> = every group shows.</param>
public sealed record GraphFilterOptions(
    string Search      = "",
    bool   LocalGraph  = false,
    string ActiveFile  = "",
    int    LocalDepth  = 1,
    int    MinDegree   = 0,
    IReadOnlySet<int>? HiddenGroups = null);

/// <summary>
/// Decides which nodes the graph should show. Pure: takes the model plus the options and
/// returns the surviving ids — it never touches <see cref="GraphNode.Visible"/> itself, so it
/// can be exercised without a canvas (same split as <see cref="TextSearch"/>).
/// </summary>
public static class GraphFilter
{
    /// <summary>
    /// Applies the filters in a fixed order, because the order changes the answer:
    /// <list type="number">
    /// <item>switched-off groups and the degree floor carve out the working subgraph;</item>
    /// <item>local mode walks <c>LocalDepth</c> hops from the active note THROUGH that subgraph;</item>
    /// <item>the search text narrows whatever survived.</item>
    /// </list>
    /// The active note is exempt from both the degree floor and the group switches — otherwise
    /// turning on local mode while sitting on an orphan (or on a note in a hidden folder) would
    /// blank the screen instead of showing "this note links nowhere".
    /// </summary>
    public static HashSet<string> VisibleIds(
        IReadOnlyList<GraphNode> nodes,
        IReadOnlyList<GraphLink> links,
        GraphFilterOptions options)
    {
        var active = options.ActiveFile ?? string.Empty;
        var hidden = options.HiddenGroups;

        // ── 1. Group switches + degree floor ──
        var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodes)
        {
            if (active.Length > 0 && n.Id.Equals(active, StringComparison.OrdinalIgnoreCase))
            {
                kept.Add(n.Id);
                continue;
            }
            if (hidden is not null && hidden.Contains(n.Group)) continue;
            if (n.Degree < options.MinDegree) continue;
            kept.Add(n.Id);
        }

        // ── 2. Local neighbourhood ──
        if (options.LocalGraph && active.Length > 0)
            kept = Neighbourhood(links, active, Math.Max(1, options.LocalDepth), kept);

        // ── 3. Search text ──
        var q = (options.Search ?? string.Empty).Trim();
        if (q.Length > 0)
        {
            var byId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in nodes) byId[n.Id] = n.Label;

            kept.RemoveWhere(id =>
                !byId.TryGetValue(id, out var label) ||
                !label.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        return kept;
    }

    /// <summary>
    /// Breadth-first walk of <paramref name="depth"/> hops from <paramref name="start"/>, moving only
    /// through nodes still in <paramref name="allowed"/>. Returns the reached set (start included).
    /// </summary>
    private static HashSet<string> Neighbourhood(
        IReadOnlyList<GraphLink> links,
        string start,
        int depth,
        HashSet<string> allowed)
    {
        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in links)
        {
            Add(adjacency, l.Source.Id, l.Target.Id);
            Add(adjacency, l.Target.Id, l.Source.Id);
        }

        var reached  = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { start };
        var frontier = new List<string> { start };

        for (int hop = 0; hop < depth && frontier.Count > 0; hop++)
        {
            var next = new List<string>();
            foreach (var id in frontier)
            {
                if (!adjacency.TryGetValue(id, out var neighbours)) continue;
                foreach (var nb in neighbours)
                    if (allowed.Contains(nb) && reached.Add(nb))
                        next.Add(nb);
            }
            frontier = next;
        }

        return reached;

        static void Add(Dictionary<string, List<string>> map, string from, string to)
        {
            if (!map.TryGetValue(from, out var list)) map[from] = list = [];
            list.Add(to);
        }
    }
}
