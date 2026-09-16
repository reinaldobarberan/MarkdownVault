using MarkdownVault.Models;

namespace MarkdownVault.Services;

/// <summary>
/// A world-space point. Local to the services layer so the layout maths stays WPF-free.
/// <c>Z</c> is the depth axis and is 0 everywhere in flat mode.
/// </summary>
public readonly record struct GraphPoint(double X, double Y, double Z = 0);

/// <summary>One area of the vault: a top-level folder plus how many notes live under it.</summary>
/// <param name="Index">Stable position in the group list — also the palette index.</param>
/// <param name="Name">Folder name, or <see cref="GraphGrouping.RootName"/> for the vault root.</param>
/// <param name="Count">Notes in the group.</param>
public sealed record GraphGroup(int Index, string Name, int Count);

/// <summary>
/// Splits the vault into visual areas by top-level folder and hands out the anchor each area
/// gravitates to. Pure and deterministic: same vault in, same group indices out — which is the
/// whole point, because an index that shuffled between rebuilds would repaint the graph in
/// different colours every time the user switched tabs.
/// </summary>
public static class GraphGrouping
{
    /// <summary>Display name for notes sitting directly in the vault root.</summary>
    public const string RootName = "(raíz)";

    /// <summary>
    /// Assigns <see cref="GraphNode.Group"/> to every node and returns the groups, ordered with
    /// the root first and the rest alphabetically. Ordering by NAME (not by first appearance or
    /// by size) is what makes the colours stable: adding a note must never recolour the vault.
    /// </summary>
    public static IReadOnlyList<GraphGroup> Assign(IReadOnlyList<GraphNode> nodes)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodes)
        {
            var key = n.TopFolder;
            counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
        }

        var names = counts.Keys
            .OrderBy(k => k.Length == 0 ? 0 : 1)            // root group always first
            .ThenBy(k => k, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var indexOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < names.Count; i++) indexOf[names[i]] = i;

        foreach (var n in nodes)
            n.Group = indexOf[n.TopFolder];

        return names
            .Select((name, i) => new GraphGroup(i, name.Length == 0 ? RootName : name, counts[name]))
            .ToList();
    }

    /// <summary>
    /// Places one anchor per group, so clustering pulls each area to its OWN centre instead of
    /// everyone piling onto the origin. Using fixed anchors rather than live centroids is
    /// deliberate: centroids can drift into each other and collapse back into a single blob, while
    /// fixed anchors guarantee the areas stay apart no matter how the simulation settles.
    ///
    /// <paramref name="spatial"/> false lays them on a ring in the Z=0 plane (flat view); true
    /// spreads them over a sphere, which is the whole reason 3D is worth anything — a sphere fits
    /// far more areas at a comfortable distance from each other than a circle of the same radius.
    /// </summary>
    public static IReadOnlyList<GraphPoint> Anchors(int groupCount, int nodeCount, bool spatial = false)
    {
        if (groupCount <= 0) return [];
        if (groupCount == 1) return [new GraphPoint(0, 0, 0)];

        double radius = AnchorRadius(groupCount, nodeCount, spatial);

        var anchors = new List<GraphPoint>(groupCount);

        if (!spatial)
        {
            for (int i = 0; i < groupCount; i++)
            {
                double a = (double)i / groupCount * Math.PI * 2 - Math.PI / 2;   // start at 12 o'clock
                anchors.Add(new GraphPoint(Math.Cos(a) * radius, Math.Sin(a) * radius, 0));
            }
            return anchors;
        }

        foreach (var p in FibonacciSphere(groupCount))
            anchors.Add(new GraphPoint(p.X * radius, p.Y * radius, p.Z * radius));

        return anchors;
    }

    /// <summary>
    /// How far out the anchors sit. Two pressures: a big vault needs more room so its areas do not
    /// overlap, and MANY areas need more room so neighbouring anchors do not end up on top of each
    /// other. Take whichever demands more.
    ///
    /// The crowding term is where the two modes genuinely differ. On a ring the room available
    /// grows with the radius (2πr); on a sphere it grows with the radius SQUARED (4πr²), so the
    /// same number of areas needs a far smaller sphere than circle. That is the extra space 3D buys.
    /// </summary>
    private static double AnchorRadius(int groupCount, int nodeCount, bool spatial)
    {
        double bySize = 34 * Math.Sqrt(Math.Max(nodeCount, 1));

        double byCrowd = spatial
            ? Math.Sqrt(groupCount * 150.0 * 150.0 / (4 * Math.PI))   // area per anchor on a sphere
            : groupCount * 150 / (2 * Math.PI);                       // arc per anchor on a ring

        return Math.Clamp(Math.Max(bySize, byCrowd), 220, 2000);
    }

    /// <summary>
    /// Evenly-ish spaced points on the unit sphere, by the golden-angle spiral. Naïve lat/long
    /// spacing bunches everything at the poles; this does not, and it needs no iteration.
    /// </summary>
    public static IEnumerable<GraphPoint> FibonacciSphere(int count)
    {
        if (count <= 0) yield break;
        if (count == 1) { yield return new GraphPoint(0, 0, 0); yield break; }

        double goldenAngle = Math.PI * (3 - Math.Sqrt(5));

        for (int i = 0; i < count; i++)
        {
            double y     = 1 - 2.0 * i / (count - 1);           // from +1 down to -1
            double ring  = Math.Sqrt(Math.Max(0, 1 - y * y));   // radius of the slice at this height
            double theta = goldenAngle * i;
            yield return new GraphPoint(Math.Cos(theta) * ring, y, Math.Sin(theta) * ring);
        }
    }
}
