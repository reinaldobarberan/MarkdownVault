using MarkdownVault.Services;
using Xunit;
using static MarkdownVault.Tests.GraphTestData;

namespace MarkdownVault.Tests;

/// <summary>
/// Covers <see cref="GraphLayout"/>, the policy layer: which notes get simulated, where each island
/// is told to sit inside its folder, and where the link-less notes are parked.
/// </summary>
public class GraphLayoutTests
{
    [Fact]
    public void Notes_with_links_are_simulated_and_link_less_ones_are_parked()
    {
        var (nodes, links) = Build(
            Note("a.md", "b.md"),
            Note("b.md"),
            Note("suelta.md"));

        GraphLayout.Assign(nodes, links);

        Assert.False(nodes.Single(n => n.Id == "a.md").Parked);
        Assert.False(nodes.Single(n => n.Id == "b.md").Parked);
        Assert.True (nodes.Single(n => n.Id == "suelta.md").Parked);
    }

    [Fact]
    public void A_parked_note_is_placed_immediately_not_just_aimed_at_a_target()
    {
        // It is never simulated, so if its position were left at the seed it would sit wherever
        // GraphService dropped it until something else moved it — which nothing ever would.
        var (nodes, links) = Build(Note("suelta.md"));

        GraphLayout.Assign(nodes, links);
        var orphan = nodes[0];

        Assert.Equal(orphan.TargetX, orphan.X, precision: 9);
        Assert.Equal(orphan.TargetY, orphan.Y, precision: 9);
        Assert.True(Math.Sqrt(orphan.X * orphan.X + orphan.Y * orphan.Y) > 1,
            "una nota aparcada tiene que quedar en su asiento, no en el origen");
    }

    [Fact]
    public void Parked_notes_never_share_a_seat()
    {
        var spec = Enumerable.Range(0, 40).Select(i => Note($"suelta{i:00}.md")).ToArray();
        var (nodes, links) = Build(spec);

        GraphLayout.Assign(nodes, links);

        var seats = nodes.Select(n => (Math.Round(n.X, 3), Math.Round(n.Y, 3))).ToList();
        Assert.Equal(seats.Count, seats.Distinct().Count());
    }

    [Fact]
    public void Notes_of_the_same_island_are_pulled_to_the_same_spot()
    {
        var (nodes, links) = Build(
            Note("docs/a.md", "docs/b.md"),
            Note("docs/b.md"));

        GraphLayout.Assign(nodes, links);

        var a = nodes.Single(n => n.Id == "docs/a.md");
        var b = nodes.Single(n => n.Id == "docs/b.md");
        Assert.Equal(a.TargetX, b.TargetX, precision: 9);
        Assert.Equal(a.TargetY, b.TargetY, precision: 9);
    }

    [Fact]
    public void Two_islands_inside_one_folder_are_pulled_to_different_spots()
    {
        // This is the whole point of the island layer: a folder holding several unrelated little
        // clusters must show several clusters, not one blob.
        var (nodes, links) = Build(
            Note("docs/a.md", "docs/b.md"), Note("docs/b.md"),
            Note("docs/x.md", "docs/y.md"), Note("docs/y.md"));

        GraphLayout.Assign(nodes, links);

        var first  = nodes.Single(n => n.Id == "docs/a.md");
        var second = nodes.Single(n => n.Id == "docs/x.md");
        Assert.True(
            Math.Abs(first.TargetX - second.TargetX) > 1 ||
            Math.Abs(first.TargetY - second.TargetY) > 1,
            "dos islas de la misma carpeta terminaron en el mismo punto");
    }

    [Fact]
    public void A_folder_with_a_single_island_keeps_its_anchor_without_being_pushed_off_centre()
    {
        var (nodes, links) = Build(
            Note("docs/a.md", "docs/b.md"), Note("docs/b.md"),
            Note("otros/x.md", "otros/y.md"), Note("otros/y.md"));

        GraphLayout.Assign(nodes, links);

        var anchors = GraphGrouping.Anchors(2, nodes.Count);
        var docs = nodes.Single(n => n.Id == "docs/a.md");
        var expected = anchors[docs.Group];

        Assert.Equal(expected.X, docs.TargetX, precision: 6);
        Assert.Equal(expected.Y, docs.TargetY, precision: 6);
    }

    [Fact]
    public void Parked_notes_sit_outside_the_notes_that_are_simulated()
    {
        var (nodes, links) = Build(
            Note("docs/a.md", "docs/b.md"),
            Note("docs/b.md"),
            Note("docs/suelta.md"));

        GraphLayout.Assign(nodes, links);

        double linked = Distance(nodes.Single(n => n.Id == "docs/a.md").TargetX,
                                 nodes.Single(n => n.Id == "docs/a.md").TargetY);
        double parked = Distance(nodes.Single(n => n.Id == "docs/suelta.md").TargetX,
                                 nodes.Single(n => n.Id == "docs/suelta.md").TargetY);

        Assert.True(parked > linked, $"las sueltas deberían quedar por fuera ({parked} vs {linked})");

        static double Distance(double x, double y) => Math.Sqrt(x * x + y * y);
    }

    [Fact]
    public void Laying_out_an_empty_graph_does_not_throw()
    {
        var groups = GraphLayout.Assign([], []);
        Assert.Empty(groups);
    }

    // ── Spatial (3D) layout ──

    [Fact]
    public void A_flat_layout_leaves_every_target_on_the_plane()
    {
        var (nodes, links) = Build(
            Note("docs/a.md", "docs/b.md"), Note("docs/b.md"),
            Note("otros/x.md", "otros/y.md"), Note("otros/y.md"),
            Note("suelta.md"));

        GraphLayout.Assign(nodes, links, spatial: false);

        Assert.All(nodes, n => Assert.Equal(0, n.TargetZ, precision: 9));
    }

    [Fact]
    public void A_spatial_layout_pushes_folders_off_the_plane()
    {
        var (nodes, links) = Build(
            Note("a1/a.md", "a1/b.md"), Note("a1/b.md"),
            Note("b2/x.md", "b2/y.md"), Note("b2/y.md"),
            Note("c3/p.md", "c3/q.md"), Note("c3/q.md"));

        GraphLayout.Assign(nodes, links, spatial: true);

        Assert.Contains(nodes, n => Math.Abs(n.TargetZ) > 1);
    }

    [Fact]
    public void Link_less_notes_stay_flat_even_in_a_spatial_layout()
    {
        // A sphere of orphans would wrap the graph and hide the connected notes from every angle.
        // The halo has to be something you look THROUGH while orbiting.
        var (nodes, links) = Build(
            Note("docs/a.md", "docs/b.md"), Note("docs/b.md"),
            Note("otros/x.md", "otros/y.md"), Note("otros/y.md"),
            Note("suelta1.md"), Note("suelta2.md"), Note("suelta3.md"));

        GraphLayout.Assign(nodes, links, spatial: true);

        foreach (var orphan in nodes.Where(n => n.Parked))
        {
            Assert.Equal(0, orphan.TargetZ, precision: 9);
            Assert.Equal(0, orphan.Z, precision: 9);
        }
    }

    [Fact]
    public void Switching_between_flat_and_spatial_never_drops_two_folders_on_one_spot()
    {
        // This is why the toggle re-runs the layout instead of just tilting the camera: flattening
        // a sphere of anchors onto the plane WOULD collide.
        var (nodes, links) = Build(
            Note("a1/a.md", "a1/b.md"), Note("a1/b.md"),
            Note("b2/x.md", "b2/y.md"), Note("b2/y.md"),
            Note("c3/p.md", "c3/q.md"), Note("c3/q.md"),
            Note("d4/m.md", "d4/n.md"), Note("d4/n.md"));

        foreach (bool spatial in new[] { true, false, true })
        {
            GraphLayout.Assign(nodes, links, spatial);

            var spots = nodes
                .Where(n => !n.Parked)
                .Select(n => (n.Group, X: Math.Round(n.TargetX, 3), Y: Math.Round(n.TargetY, 3), Z: Math.Round(n.TargetZ, 3)))
                .Distinct()
                .ToList();

            var positions = spots.Select(s => (s.X, s.Y, s.Z)).ToList();
            Assert.Equal(positions.Count, positions.Distinct().Count());
        }
    }
}
