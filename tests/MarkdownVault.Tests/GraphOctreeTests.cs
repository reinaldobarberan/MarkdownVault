using MarkdownVault.Models;
using MarkdownVault.Services;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Covers <see cref="GraphOctree"/>, the Barnes-Hut approximation that replaced the every-pair
/// repulsion loop.
///
/// An approximation you cannot check is an approximation you cannot trust: a subtly wrong tree
/// produces a layout that merely LOOKS plausible, which is the worst kind of bug. So the anchor of
/// this file is θ = 0 — the setting that refuses to approximate anything — measured against a
/// brute-force loop written out in full below.
/// </summary>
public class GraphOctreeTests
{
    private const double Strength = 14000;

    [Fact]
    public void With_theta_zero_it_matches_the_brute_force_loop_exactly()
    {
        var bodies = Cloud(120, seed: 20260915, flat: false);
        var tree = new GraphOctree();
        tree.Build(bodies);

        for (int i = 0; i < bodies.Count; i++)
        {
            tree.Repulsion(i, Strength, theta: 0, out double fx, out double fy, out double fz);
            var (ex, ey, ez) = BruteForce(bodies, i, Strength);

            Assert.Equal(ex, fx, precision: 6);
            Assert.Equal(ey, fy, precision: 6);
            Assert.Equal(ez, fz, precision: 6);
        }
    }

    [Fact]
    public void With_theta_zero_it_is_exact_for_a_flat_layout_too()
    {
        // Flat mode puts every node on Z = 0. That collapses the octree onto four live octants,
        // which is exactly the degenerate shape most likely to expose an indexing mistake.
        var bodies = Cloud(80, seed: 3, flat: true);
        var tree = new GraphOctree();
        tree.Build(bodies);

        for (int i = 0; i < bodies.Count; i++)
        {
            tree.Repulsion(i, Strength, theta: 0, out double fx, out double fy, out double fz);
            var (ex, ey, _) = BruteForce(bodies, i, Strength);

            Assert.Equal(ex, fx, precision: 6);
            Assert.Equal(ey, fy, precision: 6);
            Assert.Equal(0, fz, precision: 9);     // nothing should push a flat layout off-plane
        }
    }

    [Fact]
    public void At_the_working_theta_the_error_stays_negligible()
    {
        var bodies = Cloud(400, seed: 7, flat: false);
        var tree = new GraphOctree();
        tree.Build(bodies);

        double totalError = 0, totalMagnitude = 0;

        for (int i = 0; i < bodies.Count; i++)
        {
            tree.Repulsion(i, Strength, theta: 0.7, out double fx, out double fy, out double fz);
            var (ex, ey, ez) = BruteForce(bodies, i, Strength);

            double dx = fx - ex, dy = fy - ey, dz = fz - ez;
            totalError     += Math.Sqrt(dx * dx + dy * dy + dz * dz);
            totalMagnitude += Math.Sqrt(ex * ex + ey * ey + ez * ez);
        }

        double relative = totalError / totalMagnitude;
        Assert.True(relative < 0.05, $"error relativo medio {relative:P2}, esperado bajo 5%");
    }

    [Fact]
    public void Two_bodies_push_each_other_apart_with_equal_and_opposite_force()
    {
        var bodies = new List<GraphNode> { Node("a", 0, 0, 0), Node("b", 10, 0, 0) };
        var tree = new GraphOctree();
        tree.Build(bodies);

        tree.Repulsion(0, Strength, theta: 0.7, out double ax, out double ay, out double az);
        tree.Repulsion(1, Strength, theta: 0.7, out double bx, out double by, out double bz);

        Assert.Equal(-Strength / 100, ax, precision: 6);   // a is pushed toward -x
        Assert.Equal(0, ay, precision: 9);
        Assert.Equal(0, az, precision: 9);
        Assert.Equal(-ax, bx, precision: 6);
        Assert.Equal(-ay, by, precision: 9);
        Assert.Equal(-az, bz, precision: 9);
    }

    [Fact]
    public void Depth_is_a_real_axis_not_a_decoration()
    {
        // Two bodies separated only along Z must repel along Z. If the tree were still a quadtree
        // wearing a Z field, this would come out zero.
        var bodies = new List<GraphNode> { Node("a", 0, 0, 0), Node("b", 0, 0, 10) };
        var tree = new GraphOctree();
        tree.Build(bodies);

        tree.Repulsion(0, Strength, theta: 0, out double fx, out double fy, out double fz);

        Assert.Equal(0, fx, precision: 9);
        Assert.Equal(0, fy, precision: 9);
        Assert.Equal(-Strength / 100, fz, precision: 6);
    }

    [Fact]
    public void A_lone_body_feels_nothing()
    {
        var tree = new GraphOctree();
        tree.Build([Node("solo", 5, 5, 5)]);

        tree.Repulsion(0, Strength, theta: 0.7, out double fx, out double fy, out double fz);

        Assert.Equal(0, fx);
        Assert.Equal(0, fy);
        Assert.Equal(0, fz);
    }

    [Fact]
    public void An_empty_tree_answers_zero_instead_of_throwing()
    {
        var tree = new GraphOctree();
        tree.Build([]);

        tree.Repulsion(0, Strength, theta: 0.7, out double fx, out double fy, out double fz);

        Assert.Equal(0, tree.Count);
        Assert.Equal(0, fx);
        Assert.Equal(0, fy);
        Assert.Equal(0, fz);
    }

    [Fact]
    public void Bodies_stacked_on_the_exact_same_spot_do_not_subdivide_forever()
    {
        // Without the depth cap this hangs: no amount of halving ever separates two identical
        // coordinates. Two pinned notes dropped on the same pixel would be enough to trigger it.
        var bodies = Enumerable.Range(0, 8).Select(i => Node($"n{i}", 3, 3, 3)).ToList();
        var tree = new GraphOctree();

        tree.Build(bodies);
        tree.Repulsion(0, Strength, theta: 0.7, out double fx, out double fy, out double fz);

        Assert.Equal(8, tree.Count);
        Assert.False(double.IsNaN(fx) || double.IsInfinity(fx));
        Assert.False(double.IsNaN(fy) || double.IsInfinity(fy));
        Assert.False(double.IsNaN(fz) || double.IsInfinity(fz));
    }

    [Fact]
    public void Rebuilding_over_a_smaller_set_forgets_the_previous_bodies()
    {
        var tree = new GraphOctree();
        tree.Build(Cloud(50, seed: 1, flat: false));

        var two = new List<GraphNode> { Node("a", 0, 0, 0), Node("b", 10, 0, 0) };
        tree.Build(two);

        tree.Repulsion(0, Strength, theta: 0, out double fx, out _, out _);

        Assert.Equal(2, tree.Count);
        Assert.Equal(-Strength / 100, fx, precision: 6);
    }

    // ── Reference implementation: the loop Barnes-Hut replaced, kept verbatim ──

    private static (double Fx, double Fy, double Fz) BruteForce(
        IReadOnlyList<GraphNode> bodies, int index, double strength)
    {
        double fx = 0, fy = 0, fz = 0;
        var a = bodies[index];

        for (int j = 0; j < bodies.Count; j++)
        {
            if (j == index) continue;
            var b = bodies[j];
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            double d2 = dx * dx + dy * dy + dz * dz;
            if (d2 < 0.01) d2 = 0.01;
            double d = Math.Sqrt(d2);
            double f = strength / d2;
            fx += dx / d * f;
            fy += dy / d * f;
            fz += dz / d * f;
        }

        return (fx, fy, fz);
    }

    private static List<GraphNode> Cloud(int count, int seed, bool flat)
    {
        var random = new Random(seed);
        var bodies = new List<GraphNode>(count);
        for (int i = 0; i < count; i++)
            bodies.Add(Node($"n{i}",
                random.NextDouble() * 2000 - 1000,
                random.NextDouble() * 2000 - 1000,
                flat ? 0 : random.NextDouble() * 2000 - 1000));
        return bodies;
    }

    private static GraphNode Node(string name, double x, double y, double z) => new()
    {
        Id       = $"{name}.md",
        Label    = name,
        FullPath = $@"C:\vault\{name}.md",
        X        = x,
        Y        = y,
        Z        = z
    };
}
