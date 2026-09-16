using MarkdownVault.Models;
using MarkdownVault.Services;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Covers <see cref="GraphCollision"/>. The question these answer is the one a screenshot cannot:
/// when nodes end up drawn on top of each other, is the collision pass failing, or is it working
/// and something else (the zoom, the layout spacing, plain 3D occlusion) is to blame?
/// </summary>
public class GraphCollisionTests
{
    private const double Pad = 6.0;
    private const double Stiffness = 0.5;

    [Fact]
    public void Nodes_that_are_not_touching_are_left_alone()
    {
        var a = Node("a", 0, 0, 0);
        var b = Node("b", 500, 0, 0);

        int separated = new GraphCollision().Resolve([a, b], Pad, Stiffness);

        Assert.Equal(0, separated);
        Assert.Equal(0, a.X, precision: 9);
        Assert.Equal(500, b.X, precision: 9);
    }

    [Fact]
    public void An_overlapping_pair_comes_apart_and_stays_apart()
    {
        var a = Node("a", 0, 0, 0);
        var b = Node("b", 5, 0, 0);
        var collision = new GraphCollision();

        // One pass only undoes part of the overlap by design; a handful finishes it, the same way
        // consecutive frames do.
        for (int i = 0; i < 40; i++) collision.Resolve([a, b], Pad, Stiffness);

        Assert.True(Gap(a, b) >= MinGap(a, b) - GraphCollision.Tolerance,
            $"quedaron a {Gap(a, b)}, mínimo {MinGap(a, b)}");
        Assert.Equal(0, collision.Resolve([a, b], Pad, Stiffness));
    }

    [Fact]
    public void A_pile_of_nodes_on_one_spot_untangles_completely()
    {
        // The worst case there is: everything coincident, so there is not even a direction to push
        // along until the tie-break invents one.
        var nodes = Enumerable.Range(0, 20).Select(i => Node($"n{i:00}", 0, 0, 0)).ToList();
        var collision = new GraphCollision();

        for (int i = 0; i < 400; i++) collision.Resolve(nodes, Pad, Stiffness);

        Assert.Equal(0, collision.Resolve(nodes, Pad, Stiffness));
    }

    [Fact]
    public void Overlaps_are_found_across_grid_cell_boundaries()
    {
        // The grid is the part most likely to be subtly wrong: a pair straddling a cell edge must
        // still be compared. Place them either side of a boundary along every axis at once.
        var collision = new GraphCollision();
        double cell = 2 * Node("x", 0, 0, 0).Radius + Pad;

        var a = Node("a", cell - 0.5, cell - 0.5, cell - 0.5);
        var b = Node("b", cell + 0.5, cell + 0.5, cell + 0.5);

        Assert.Equal(1, collision.Resolve([a, b], Pad, Stiffness));
    }

    [Fact]
    public void Separation_works_along_the_depth_axis_too()
    {
        // If the pass had stayed two-dimensional, nodes stacked in Z would never come apart.
        var a = Node("a", 0, 0, 0);
        var b = Node("b", 0, 0, 4);
        var collision = new GraphCollision();

        for (int i = 0; i < 40; i++) collision.Resolve([a, b], Pad, Stiffness);

        Assert.True(Math.Abs(a.Z - b.Z) > 5, $"no se separaron en profundidad: {a.Z} vs {b.Z}");
        Assert.Equal(0, collision.Resolve([a, b], Pad, Stiffness));
    }

    [Fact]
    public void A_hand_placed_node_does_not_move_and_the_other_one_yields_fully()
    {
        var pinned = Node("pinned", 0, 0, 0);
        pinned.Fx = 0; pinned.Fy = 0; pinned.Fz = 0;
        var free = Node("free", 5, 0, 0);

        var collision = new GraphCollision();
        for (int i = 0; i < 40; i++) collision.Resolve([pinned, free], Pad, Stiffness);

        Assert.Equal(0, pinned.X, precision: 9);
        Assert.Equal(0, pinned.Y, precision: 9);
        Assert.Equal(0, pinned.Z, precision: 9);
        Assert.True(Gap(pinned, free) >= MinGap(pinned, free) - GraphCollision.Tolerance);
    }

    [Fact]
    public void Two_hand_placed_nodes_are_left_where_the_user_put_them()
    {
        var a = Node("a", 0, 0, 0); a.Fx = 0; a.Fy = 0; a.Fz = 0;
        var b = Node("b", 5, 0, 0); b.Fx = 5; b.Fy = 0; b.Fz = 0;

        new GraphCollision().Resolve([a, b], Pad, Stiffness);

        Assert.Equal(0, a.X, precision: 9);
        Assert.Equal(5, b.X, precision: 9);
    }

    [Fact]
    public void A_big_hub_claims_more_room_than_a_small_note()
    {
        // The whole reason this pass exists: repulsion is blind to size.
        var hub   = Node("hub",   0, 0, 0, radius: GraphMetrics.MaxRadius);
        var small = Node("small", 5, 0, 0, radius: GraphMetrics.MinRadius);

        var collision = new GraphCollision();
        for (int i = 0; i < 60; i++) collision.Resolve([hub, small], Pad, Stiffness);

        Assert.True(Gap(hub, small) >= GraphMetrics.MaxRadius + GraphMetrics.MinRadius + Pad - GraphCollision.Tolerance,
            $"el hub no reclamó su espacio: {Gap(hub, small)}");
    }

    [Fact]
    public void A_single_node_or_none_is_a_no_op()
    {
        var collision = new GraphCollision();
        Assert.Equal(0, collision.Resolve([], Pad, Stiffness));
        Assert.Equal(0, collision.Resolve([Node("solo", 1, 2, 3)], Pad, Stiffness));
    }

    // ── helpers ──

    private static double Gap(GraphNode a, GraphNode b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static double MinGap(GraphNode a, GraphNode b) =>
        a.Radius + b.Radius + Pad;

    // Radius is set straight rather than derived from a degree: these tests are about the
    // separation maths, not about how GraphMetrics normalises sizes (that has its own file).
    private static GraphNode Node(string name, double x, double y, double z, double radius = 8) => new()
    {
        Id       = $"{name}.md",
        Label    = name,
        FullPath = $@"C:\vault\{name}.md",
        X = x, Y = y, Z = z,
        Radius = radius
    };
}
