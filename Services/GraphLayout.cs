using MarkdownVault.Models;

namespace MarkdownVault.Services;

/// <summary>
/// The single entry point that turns a freshly built graph into a laid-out one: it groups the
/// notes by folder, finds the islands inside each folder, and stamps every node with the point
/// gravity should pull it toward. Pure — no WPF, no simulation, no rendering — so the whole
/// layout policy can be asserted in a headless test.
///
/// Two levels of structure, deliberately:
/// <list type="number">
/// <item>a folder gets an anchor away from the centre (its area of the vault);</item>
/// <item>each island INSIDE that folder gets its own spot around that anchor, so a folder holding
///       twelve unrelated little clusters shows twelve clusters instead of one blob.</item>
/// </list>
/// Notes with no links at all are not simulated at all: they get a fixed seat on rings outside
/// everything, which both declutters the middle and shrinks the expensive force loop.
/// </summary>
public static class GraphLayout
{
    /// <summary>World-space gap between two seats on the orphan rings.</summary>
    private const double OrphanSpacing = 46;
    /// <summary>Distance between one orphan ring and the next one out.</summary>
    private const double OrphanRingGap = 58;
    /// <summary>Clearance between the outermost folder anchor and the first orphan ring.</summary>
    private const double OrphanMargin = 260;

    /// <summary>
    /// Assigns groups, islands and layout targets in one pass and returns the groups for the
    /// legend. Call it once per graph build — or when the view switches between flat and spatial,
    /// which changes where every anchor goes — but never per frame.
    /// </summary>
    /// <param name="spatial">
    /// <c>true</c> lays the vault out in three dimensions; <c>false</c> keeps everything on the
    /// Z=0 plane, which is the flat view the graph had before 3D existed.
    /// </param>
    /// <param name="nodeScale">The user's node-size dial. Feeds the radii, which the collision
    /// pass and the camera framing both read — not just the drawing.</param>
    public static IReadOnlyList<GraphGroup> Assign(
        IReadOnlyList<GraphNode> nodes,
        IReadOnlyList<GraphLink> links,
        bool spatial = false,
        double nodeScale = 1.0)
    {
        var groups = GraphGrouping.Assign(nodes);
        GraphComponents.Assign(nodes, links);
        GraphMetrics.Assign(nodes, nodeScale);

        var folderAnchors = GraphGrouping.Anchors(groups.Count, nodes.Count, spatial);
        double folderRadius = 0;
        foreach (var a in folderAnchors)
            folderRadius = Math.Max(folderRadius, Math.Sqrt(a.X * a.X + a.Y * a.Y + a.Z * a.Z));

        PlaceLinked(nodes, folderAnchors, spatial);
        PlaceOrphans(nodes, folderRadius + OrphanMargin);

        return groups;
    }

    /// <summary>
    /// Gives every island its own spot around its folder's anchor. A folder with a single island
    /// keeps the anchor itself (offset zero) — spinning a lone island off-centre would just make
    /// the folder look lopsided for no reason.
    /// </summary>
    private static void PlaceLinked(
        IReadOnlyList<GraphNode> nodes,
        IReadOnlyList<GraphPoint> folderAnchors,
        bool spatial)
    {
        // folder → its islands → the nodes in each
        var byFolder = new Dictionary<int, Dictionary<int, List<GraphNode>>>();
        foreach (var n in nodes)
        {
            if (n.Component == GraphComponents.Orphan) continue;
            if (!byFolder.TryGetValue(n.Group, out var islands))
                byFolder[n.Group] = islands = [];
            if (!islands.TryGetValue(n.Component, out var members))
                islands[n.Component] = members = [];
            members.Add(n);
        }

        foreach (var (group, islands) in byFolder)
        {
            var anchor = group >= 0 && group < folderAnchors.Count
                ? folderAnchors[group]
                : new GraphPoint(0, 0, 0);

            int folderNodes = islands.Values.Sum(m => m.Count);
            var ordered = islands.Keys.OrderBy(c => c).ToList();
            double ring = IslandRingRadius(folderNodes, ordered.Count);

            var offsets = IslandOffsets(ordered.Count, ring, spatial);

            for (int k = 0; k < ordered.Count; k++)
            {
                var offset = offsets[k];
                foreach (var n in islands[ordered[k]])
                {
                    n.TargetX = anchor.X + offset.X;
                    n.TargetY = anchor.Y + offset.Y;
                    n.TargetZ = anchor.Z + offset.Z;
                    n.Parked  = false;
                }
            }
        }
    }

    /// <summary>
    /// Where each island sits relative to its folder's anchor: a circle in flat mode, a sphere in
    /// spatial mode. A single island gets no offset at all.
    /// </summary>
    private static IReadOnlyList<GraphPoint> IslandOffsets(int islandCount, double ring, bool spatial)
    {
        var offsets = new List<GraphPoint>(islandCount);

        if (ring <= 0 || islandCount <= 1)
        {
            for (int i = 0; i < islandCount; i++) offsets.Add(new GraphPoint(0, 0, 0));
            return offsets;
        }

        if (spatial)
        {
            foreach (var p in GraphGrouping.FibonacciSphere(islandCount))
                offsets.Add(new GraphPoint(p.X * ring, p.Y * ring, p.Z * ring));
            return offsets;
        }

        for (int k = 0; k < islandCount; k++)
        {
            double a = (double)k / islandCount * Math.PI * 2 - Math.PI / 2;
            offsets.Add(new GraphPoint(Math.Cos(a) * ring, Math.Sin(a) * ring, 0));
        }
        return offsets;
    }

    /// <summary>
    /// How far an island sits from its folder's anchor. Zero for a folder with one island; for
    /// several it is driven by how crowded the folder is AND by how many islands must fit around
    /// the circle, whichever asks for more room.
    /// </summary>
    private static double IslandRingRadius(int folderNodeCount, int islandCount)
    {
        if (islandCount <= 1) return 0;
        double byCrowd  = 28 * Math.Sqrt(Math.Max(folderNodeCount, 1));
        double bySpread = islandCount * 110 / (2 * Math.PI);
        return Math.Clamp(Math.Max(byCrowd, bySpread), 90, 900);
    }

    /// <summary>
    /// Seats every link-less note on concentric rings outside the graph, ordered by folder and
    /// then by name. Ordering by folder makes the rings come out banded by colour, so the halo
    /// reads as "here are the loose ends, grouped by where they live" instead of as noise.
    /// Positions are written straight to X/Y/Z as well as to the target: a parked note is placed,
    /// not pulled, so it must be correct on the very first frame.
    ///
    /// They stay FLAT (Z = 0) even in spatial mode, on purpose. A sphere of orphans would enclose
    /// the graph and hide the connected notes from every viewing angle; a flat halo is something
    /// you look through, and it keeps the middle of the scene readable while you orbit.
    /// </summary>
    private static void PlaceOrphans(IReadOnlyList<GraphNode> nodes, double startRadius)
    {
        var orphans = nodes
            .Where(n => n.Component == GraphComponents.Orphan)
            .OrderBy(n => n.Group)
            .ThenBy(n => n.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (orphans.Count == 0) return;

        double radius = Math.Max(startRadius, 260);
        int placed = 0;

        while (placed < orphans.Count)
        {
            // Never fewer than 8 per ring, or a small radius would spawn a ring per note.
            int capacity = Math.Max(8, (int)(2 * Math.PI * radius / OrphanSpacing));
            int take = Math.Min(capacity, orphans.Count - placed);

            for (int k = 0; k < take; k++)
            {
                double a = (double)k / take * Math.PI * 2 - Math.PI / 2;
                var n = orphans[placed + k];
                n.TargetX = n.X = Math.Cos(a) * radius;
                n.TargetY = n.Y = Math.Sin(a) * radius;
                n.TargetZ = n.Z = 0;
                n.Vx = n.Vy = n.Vz = 0;
                n.Parked = true;
            }

            placed += take;
            radius += OrphanRingGap;
        }
    }
}
