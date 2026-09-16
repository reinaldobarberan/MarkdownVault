using MarkdownVault.Models;

namespace MarkdownVault.Services;

/// <summary>
/// Splits the graph into connected components — the "islands" of notes that can reach each other
/// by following links. Pure and deterministic, like the rest of the graph engine.
/// </summary>
public static class GraphComponents
{
    /// <summary>Component id of a note with no links at all.</summary>
    public const int Orphan = -1;

    /// <summary>
    /// Stamps <see cref="GraphNode.Component"/> on every node and returns how many real islands
    /// there are. Notes with zero links are NOT islands of one — they get <see cref="Orphan"/>,
    /// because they are laid out separately (nothing pulls on them, so simulating them is waste).
    ///
    /// Ids are handed out by the alphabetically-first note in each island rather than by the order
    /// the union-find happened to finish in. Without that, the same vault could number its islands
    /// differently between two rebuilds and every position would jump.
    /// </summary>
    public static int Assign(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphLink> links)
    {
        var position = new Dictionary<string, int>(nodes.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < nodes.Count; i++) position[nodes[i].Id] = i;

        var parent = new int[nodes.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;

        foreach (var l in links)
        {
            if (position.TryGetValue(l.Source.Id, out var a) &&
                position.TryGetValue(l.Target.Id, out var b))
                Union(parent, a, b);
        }

        // Bucket the linked notes by island root.
        var islands = new Dictionary<int, List<int>>();
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].Degree == 0) { nodes[i].Component = Orphan; continue; }
            int root = Find(parent, i);
            if (!islands.TryGetValue(root, out var members)) islands[root] = members = [];
            members.Add(i);
        }

        var ordered = islands.Values
            .OrderBy(members => members.Min(i => nodes[i].Id), StringComparer.Ordinal)
            .ToList();

        for (int c = 0; c < ordered.Count; c++)
            foreach (var i in ordered[c])
                nodes[i].Component = c;

        return ordered.Count;
    }

    private static int Find(int[] parent, int x)
    {
        while (parent[x] != x)
        {
            parent[x] = parent[parent[x]];   // path halving
            x = parent[x];
        }
        return x;
    }

    private static void Union(int[] parent, int a, int b)
    {
        int ra = Find(parent, a), rb = Find(parent, b);
        if (ra == rb) return;
        // Point the larger root at the smaller one so the surviving root is stable-ish; the real
        // stability guarantee comes from the alphabetical renumbering above.
        if (ra < rb) parent[rb] = ra; else parent[ra] = rb;
    }
}
