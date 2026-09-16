using MarkdownVault.Models;

namespace MarkdownVault.Tests;

/// <summary>
/// Builds in-memory graphs for the graph-engine tests. The engine (filter, grouping, components,
/// layout, quadtree) is pure — it never reads a file — so the tests never need a vault on disk.
/// </summary>
internal static class GraphTestData
{
    /// <summary>
    /// Builds nodes and undirected links from a compact spec: <c>("docs/a.md", "docs/b.md")</c>
    /// declares a note plus every note it links to. Degrees are counted exactly the way
    /// <c>GraphService</c> counts them, including the "a double link is still one edge" rule,
    /// because the filter and the layout both key off <see cref="GraphNode.Degree"/>.
    /// </summary>
    public static (List<GraphNode> Nodes, List<GraphLink> Links) Build(
        params (string Id, string[] LinksTo)[] spec)
    {
        var nodes = spec
            .Select(s => new GraphNode
            {
                Id       = s.Id,
                Label    = LabelOf(s.Id),
                FullPath = @"C:\vault\" + s.Id.Replace('/', '\\')
            })
            .ToList();

        var byId = new Dictionary<string, GraphNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodes) byId[n.Id] = n;

        var links = new List<GraphLink>();
        var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in spec)
        {
            foreach (var target in s.LinksTo)
            {
                if (!byId.TryGetValue(s.Id, out var a)) continue;
                if (!byId.TryGetValue(target, out var b)) continue;
                if (ReferenceEquals(a, b)) continue;

                var key = string.CompareOrdinal(a.Id, b.Id) < 0
                    ? $"{a.Id} {b.Id}"
                    : $"{b.Id} {a.Id}";
                if (!seen.Add(key)) continue;

                links.Add(new GraphLink { Source = a, Target = b });
                a.Degree++;
                b.Degree++;
            }
        }

        return (nodes, links);
    }

    /// <summary>Shorthand for a note that links nowhere.</summary>
    public static (string, string[]) Note(string id) => (id, []);

    /// <summary>Shorthand for a note plus its outgoing links.</summary>
    public static (string, string[]) Note(string id, params string[] linksTo) => (id, linksTo);

    private static string LabelOf(string id)
    {
        int slash = id.LastIndexOf('/');
        var name = slash < 0 ? id : id[(slash + 1)..];
        int dot = name.LastIndexOf('.');
        return dot < 0 ? name : name[..dot];
    }
}
