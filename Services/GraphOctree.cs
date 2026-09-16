using MarkdownVault.Models;

namespace MarkdownVault.Services;

/// <summary>
/// Barnes-Hut octree for the graph's repulsion force.
///
/// Repelling every node from every other node is O(n²) per frame: fine at 100 notes (~5.000 pairs),
/// unusable at 1.000 (~500.000 pairs, sixty times a second). The trick is that a distant CLUMP of
/// nodes pushes almost exactly like a single node of the same total mass sitting at the clump's
/// centre. So: put the nodes in a tree and, while walking it, stop descending as soon as a cell is
/// far enough away to be treated as one lump. The frame becomes O(n log n).
///
/// "Far enough" is the θ (theta) criterion: use the lump when <c>cellWidth / distance &lt; θ</c>.
/// θ = 0 never accepts a lump, so the walk reaches individual nodes and the result is EXACT — that
/// is what the tests compare against brute force, and it is the only reason an approximation like
/// this can be trusted at all.
///
/// Eight children instead of a quadtree's four: the graph is laid out in three dimensions, and in
/// flat mode every node simply sits at Z = 0, which the tree handles without a special case.
///
/// Pure: no WPF, no rendering. Reused across frames — call <see cref="Build"/>, then query.
/// </summary>
public sealed class GraphOctree
{
    /// <summary>
    /// Depth limit. Two nodes at the exact same spot would otherwise subdivide forever. At the cap
    /// a cell simply keeps its accumulated mass with no children, and the query treats it as one
    /// lump — the same graceful degradation the brute-force loop got from its distance clamp.
    /// </summary>
    private const int MaxDepth = 32;

    /// <summary>Distance² floor, so two coincident nodes produce a large force instead of infinity.</summary>
    private const double MinDistanceSquared = 0.01;

    private struct Cell
    {
        public double X, Y, Z, Half;        // bounds: centre + half extent
        public double SumX, SumY, SumZ;     // Σ positions of the bodies underneath
        public double Mass;                 // how many bodies underneath
        public int    Body;                 // the one body here when this is a leaf, else -1
        public int    Child;                // index of child 0; the eight are contiguous. -1 = none
    }

    private readonly List<Cell> _cells = [];
    private double[] _bx = [];
    private double[] _by = [];
    private double[] _bz = [];
    private int[]    _stack = new int[512];
    private int      _count;

    /// <summary>Bodies currently in the tree.</summary>
    public int Count => _count;

    /// <summary>
    /// (Re)builds the tree over <paramref name="bodies"/>. Positions are copied in, so every force
    /// computed during a frame sees the same snapshot even while the caller is already writing new
    /// velocities back onto the nodes.
    /// </summary>
    public void Build(IReadOnlyList<GraphNode> bodies)
    {
        _cells.Clear();
        _count = bodies.Count;
        if (_count == 0) return;

        if (_bx.Length < _count)
        {
            _bx = new double[_count];
            _by = new double[_count];
            _bz = new double[_count];
        }

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

        for (int i = 0; i < _count; i++)
        {
            double x = bodies[i].X, y = bodies[i].Y, z = bodies[i].Z;
            _bx[i] = x; _by[i] = y; _bz[i] = z;
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
            if (z < minZ) minZ = z;
            if (z > maxZ) maxZ = z;
        }

        // A cube root cell keeps every octant cubic all the way down. The 1.05 slack keeps the
        // extreme bodies off the boundary, where a >= test could send one outside the root.
        double span = Math.Max(Math.Max(maxX - minX, maxY - minY), maxZ - minZ);
        double half = Math.Max(span / 2, 1) * 1.05;

        _cells.Add(NewCell((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2, half));

        for (int i = 0; i < _count; i++) Insert(0, i, 0);
    }

    /// <summary>
    /// Repulsion felt by body <paramref name="bodyIndex"/> from every other body, as
    /// <c>Σ strength · mass / d²</c> pointing away from them. The caller scales the result into
    /// whatever units its integrator uses.
    /// </summary>
    public void Repulsion(
        int bodyIndex, double strength, double theta,
        out double fx, out double fy, out double fz)
    {
        fx = 0; fy = 0; fz = 0;
        if (_cells.Count == 0 || bodyIndex < 0 || bodyIndex >= _count) return;

        double px = _bx[bodyIndex], py = _by[bodyIndex], pz = _bz[bodyIndex];
        int top = 0;
        _stack[top++] = 0;

        while (top > 0)
        {
            var cell = _cells[_stack[--top]];
            if (cell.Mass == 0) continue;

            // Leaf holding exactly one node: exact pairwise term.
            if (cell.Body >= 0)
            {
                if (cell.Body == bodyIndex) continue;
                Accumulate(px, py, pz,
                           _bx[cell.Body], _by[cell.Body], _bz[cell.Body],
                           1, strength, ref fx, ref fy, ref fz);
                continue;
            }

            double comX = cell.SumX / cell.Mass;
            double comY = cell.SumY / cell.Mass;
            double comZ = cell.SumZ / cell.Mass;
            double dx = px - comX, dy = py - comY, dz = pz - comZ;
            double d2 = dx * dx + dy * dy + dz * dz;
            if (d2 < MinDistanceSquared) d2 = MinDistanceSquared;

            // Far enough to pass as one lump — or bottomed out at the depth cap, with nothing
            // left to descend into.
            if (cell.Child < 0 || cell.Half * 2 / Math.Sqrt(d2) < theta)
            {
                Accumulate(px, py, pz, comX, comY, comZ, cell.Mass, strength, ref fx, ref fy, ref fz);
                continue;
            }

            if (top + 8 > _stack.Length) Array.Resize(ref _stack, _stack.Length * 2);
            for (int k = 0; k < 8; k++) _stack[top++] = cell.Child + k;
        }
    }

    private static void Accumulate(
        double px, double py, double pz,
        double qx, double qy, double qz,
        double mass, double strength,
        ref double fx, ref double fy, ref double fz)
    {
        double dx = px - qx, dy = py - qy, dz = pz - qz;
        double d2 = dx * dx + dy * dy + dz * dz;
        if (d2 < MinDistanceSquared) d2 = MinDistanceSquared;
        double d = Math.Sqrt(d2);
        double f = strength * mass / d2;
        fx += dx / d * f;
        fy += dy / d * f;
        fz += dz / d * f;
    }

    /// <summary>
    /// Standard Barnes-Hut insertion. Three cases: an empty cell becomes a leaf; a cell already
    /// holding one body has to turn internal and push that sitting body down first; an already
    /// internal cell just routes the newcomer into the right octant.
    /// </summary>
    private void Insert(int cellIndex, int bodyIndex, int depth)
    {
        var cell = _cells[cellIndex];

        if (cell.Mass == 0)
        {
            cell.Mass = 1;
            cell.SumX = _bx[bodyIndex];
            cell.SumY = _by[bodyIndex];
            cell.SumZ = _bz[bodyIndex];
            cell.Body = bodyIndex;
            _cells[cellIndex] = cell;
            return;
        }

        if (depth >= MaxDepth)
        {
            cell.Mass += 1;
            cell.SumX += _bx[bodyIndex];
            cell.SumY += _by[bodyIndex];
            cell.SumZ += _bz[bodyIndex];
            cell.Body  = -1;                 // no longer a single-body leaf
            _cells[cellIndex] = cell;
            return;
        }

        int displaced = cell.Body;           // -1 when the cell was already internal
        cell.Body  = -1;
        cell.Mass += 1;
        cell.SumX += _bx[bodyIndex];
        cell.SumY += _by[bodyIndex];
        cell.SumZ += _bz[bodyIndex];
        _cells[cellIndex] = cell;

        if (cell.Child < 0) Subdivide(cellIndex);

        if (displaced >= 0) Insert(OctantOf(cellIndex, displaced),  displaced,  depth + 1);
        Insert(OctantOf(cellIndex, bodyIndex), bodyIndex, depth + 1);
    }

    /// <summary>
    /// Creates all eight children at once, contiguous in the list. Allocating them together means a
    /// cell needs a single child index instead of eight, and the query can push the whole octant
    /// range without branching on which children exist.
    /// </summary>
    private void Subdivide(int cellIndex)
    {
        var cell = _cells[cellIndex];
        double h = cell.Half / 2;

        cell.Child = _cells.Count;
        _cells[cellIndex] = cell;

        for (int k = 0; k < 8; k++)
        {
            double cx = cell.X + ((k & 1) != 0 ? h : -h);
            double cy = cell.Y + ((k & 2) != 0 ? h : -h);
            double cz = cell.Z + ((k & 4) != 0 ? h : -h);
            _cells.Add(NewCell(cx, cy, cz, h));
        }
    }

    /// <summary>Index of the child cell whose octant contains the body. One bit per axis.</summary>
    private int OctantOf(int cellIndex, int bodyIndex)
    {
        var cell = _cells[cellIndex];
        int k = (_bx[bodyIndex] >= cell.X ? 1 : 0)
              | (_by[bodyIndex] >= cell.Y ? 2 : 0)
              | (_bz[bodyIndex] >= cell.Z ? 4 : 0);
        return cell.Child + k;
    }

    private static Cell NewCell(double x, double y, double z, double half) => new()
    {
        X = x, Y = y, Z = z, Half = half,
        Body = -1, Child = -1
    };
}
