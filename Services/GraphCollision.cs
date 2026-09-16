using MarkdownVault.Models;

namespace MarkdownVault.Services;

/// <summary>
/// Keeps node spheres from being drawn on top of each other.
///
/// The repulsion force is blind to size — it treats every node as a point — so a high-degree hub
/// and a small note can settle overlapping however hard they push. This pass fixes that directly:
/// it finds pairs that are ALREADY touching and moves them apart.
///
/// Touching is a strictly local question, so it uses a uniform grid sized to the largest possible
/// overlap: each node only looks at its own cell and the twenty-six around it, which makes the
/// whole pass O(n). That is a different problem from repulsion — long range, every pair — which is
/// why that one uses <see cref="GraphOctree"/> and this one does not.
///
/// Pure: no WPF. It lives here rather than in the canvas because "do overlapping nodes actually
/// come apart?" is a question that has to be answerable by a test and not by squinting at a
/// screenshot.
/// </summary>
public sealed class GraphCollision
{
    /// <summary>
    /// Overlap, in world units, small enough to call zero.
    ///
    /// It is not cosmetic. Undoing a FRACTION of each overlap per pass (see the stiffness
    /// argument) means the gap approaches the minimum asymptotically and never reaches it, so
    /// without a tolerance the pass would report "still overlapping" forever and could never be
    /// used to decide that a layout is clean. At this size the leftover is far under one pixel at
    /// any usable zoom.
    /// </summary>
    public const double Tolerance = 0.05;

    private readonly Dictionary<long, List<int>> _grid = new();
    private readonly Stack<List<int>> _bucketPool = new();

    /// <summary>
    /// Separates every overlapping pair once, and returns how many pairs it had to touch — zero
    /// means the layout is clean.
    ///
    /// Correction is applied to POSITION, not velocity: an overlap is a fact about where things
    /// are now, and nudging a velocity would only fix it some frames later, if at all.
    /// <paramref name="stiffness"/> below 1 undoes part of each overlap per call so nodes converge
    /// instead of ping-ponging; repeated calls (one per frame) finish the job.
    /// </summary>
    public int Resolve(IReadOnlyList<GraphNode> nodes, double pad, double stiffness)
    {
        if (nodes.Count < 2) return 0;

        double maxRadius = 0;
        foreach (var n in nodes) maxRadius = Math.Max(maxRadius, n.Radius);

        // One cell wide enough that any overlapping pair is at most one cell apart per axis.
        double cellSize = 2 * maxRadius + pad;
        if (cellSize <= 0) return 0;

        foreach (var bucket in _grid.Values) { bucket.Clear(); _bucketPool.Push(bucket); }
        _grid.Clear();

        for (int i = 0; i < nodes.Count; i++)
        {
            long key = CellKey(nodes[i], cellSize);
            if (!_grid.TryGetValue(key, out var bucket))
                _grid[key] = bucket = _bucketPool.Count > 0 ? _bucketPool.Pop() : new List<int>();
            bucket.Add(i);
        }

        int separated = 0;

        for (int i = 0; i < nodes.Count; i++)
        {
            var a = nodes[i];
            long cx = (long)Math.Floor(a.X / cellSize);
            long cy = (long)Math.Floor(a.Y / cellSize);
            long cz = (long)Math.Floor(a.Z / cellSize);

            for (long ox = -1; ox <= 1; ox++)
            for (long oy = -1; oy <= 1; oy++)
            for (long oz = -1; oz <= 1; oz++)
            {
                if (!_grid.TryGetValue(Pack(cx + ox, cy + oy, cz + oz), out var bucket)) continue;
                foreach (int j in bucket)
                {
                    if (j <= i) continue;          // visit each pair once
                    if (Separate(a, nodes[j], pad, stiffness)) separated++;
                }
            }
        }

        return separated;
    }

    /// <summary>Pushes one pair apart. Returns whether they were overlapping at all.</summary>
    private static bool Separate(GraphNode a, GraphNode b, double pad, double stiffness)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        double d2 = dx * dx + dy * dy + dz * dz;

        double minD = a.Radius + b.Radius + pad;
        double settled = minD - Tolerance;
        if (d2 >= settled * settled) return false;

        // Exactly coincident: there is no direction to separate along, so invent one rather than
        // dividing by zero.
        //
        // It has to be SPREAD OVER THE SPHERE, not a fixed axis. Sending every coincident pair
        // along X strings the whole pile into a line, and a line then has to relax neighbour by
        // neighbour — hundreds of passes for twenty nodes. Scattered directions push the pile
        // apart in every direction at once and it clears almost immediately.
        //
        // The direction is derived from the two ids so it is stable frame to frame (a direction
        // that changed every frame would make coincident nodes shiver in place).
        if (d2 < 1e-9)
        {
            var (first, second) = string.CompareOrdinal(a.Id, b.Id) < 0 ? (a.Id, b.Id) : (b.Id, a.Id);
            uint hash = StableHash(first) * 31 + StableHash(second);

            double height = (hash & 0xFFFF) / 65535.0 * 2 - 1;          // cos of the polar angle
            double phi    = ((hash >> 16) & 0xFFFF) / 65535.0 * Math.PI * 2;
            double ring   = Math.Sqrt(Math.Max(0, 1 - height * height));

            double sign = string.CompareOrdinal(a.Id, b.Id) < 0 ? 1 : -1;
            dx = Math.Cos(phi) * ring * sign;
            dy = height * sign;
            dz = Math.Sin(phi) * ring * sign;
            d2 = 1;
        }

        double d  = Math.Sqrt(d2);
        double ux = dx / d, uy = dy / d, uz = dz / d;
        double push  = (minD - d) * stiffness;
        bool   aFree = !a.Pinned, bFree = !b.Pinned;

        if (aFree && bFree)
        {
            a.X += ux * push * 0.5; a.Y += uy * push * 0.5; a.Z += uz * push * 0.5;
            b.X -= ux * push * 0.5; b.Y -= uy * push * 0.5; b.Z -= uz * push * 0.5;
        }
        else if (aFree) { a.X += ux * push; a.Y += uy * push; a.Z += uz * push; }
        else if (bFree) { b.X -= ux * push; b.Y -= uy * push; b.Z -= uz * push; }
        // Both parked by hand: the user's placement wins over the simulation.

        return true;
    }

    /// <summary>
    /// FNV-1a. Deliberately NOT <c>string.GetHashCode</c>: .NET randomises string hashing per
    /// process, so that would hand out a different separation direction on every run — fine on
    /// screen, poison for a test that has to give the same answer twice.
    /// </summary>
    private static uint StableHash(string text)
    {
        uint hash = 2166136261;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return hash;
    }

    private static long CellKey(GraphNode n, double cellSize) =>
        Pack((long)Math.Floor(n.X / cellSize),
             (long)Math.Floor(n.Y / cellSize),
             (long)Math.Floor(n.Z / cellSize));

    /// <summary>
    /// Packs a cell's (x, y, z) into one key: 21 bits per axis. A shift-and-mask, NOT a hash mix —
    /// two different cells must never share a key, or nodes would be tested against the wrong
    /// neighbours. 21 bits covers ±1.000.000 cells per axis, far past any real vault.
    /// </summary>
    private static long Pack(long cx, long cy, long cz) =>
        ((cx & 0x1FFFFF) << 42) | ((cy & 0x1FFFFF) << 21) | (cz & 0x1FFFFF);
}
