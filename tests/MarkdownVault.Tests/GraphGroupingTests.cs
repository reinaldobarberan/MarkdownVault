using MarkdownVault.Services;
using Xunit;
using static MarkdownVault.Tests.GraphTestData;

namespace MarkdownVault.Tests;

/// <summary>
/// Covers <see cref="GraphGrouping"/>. The property that actually matters here is STABILITY: a
/// folder must keep the same index — and therefore the same colour and the same anchor — no matter
/// what order the notes arrive in or how many notes were added since the last build.
/// </summary>
public class GraphGroupingTests
{
    [Fact]
    public void The_root_folder_comes_first_and_the_rest_alphabetically()
    {
        var (nodes, _) = Build(
            Note("zeta/z.md"),
            Note("alfa/a.md"),
            Note("suelta.md"));

        var groups = GraphGrouping.Assign(nodes);

        Assert.Equal(new[] { GraphGrouping.RootName, "alfa", "zeta" }, groups.Select(g => g.Name));
    }

    [Fact]
    public void Each_group_counts_its_own_notes()
    {
        var (nodes, _) = Build(
            Note("docs/a.md"),
            Note("docs/b.md"),
            Note("otros/c.md"));

        var groups = GraphGrouping.Assign(nodes);

        Assert.Equal(2, groups.Single(g => g.Name == "docs").Count);
        Assert.Equal(1, groups.Single(g => g.Name == "otros").Count);
    }

    [Fact]
    public void Only_the_first_path_segment_decides_the_group()
    {
        // Grouping by the FULL folder would produce dozens of near-empty groups in a nested vault.
        var (nodes, _) = Build(
            Note("docs/guias/a.md"),
            Note("docs/api/b.md"));

        var groups = GraphGrouping.Assign(nodes);

        Assert.Single(groups);
        Assert.Equal("docs", groups[0].Name);
    }

    [Fact]
    public void The_same_folders_get_the_same_indices_whatever_order_the_notes_arrive_in()
    {
        var (first,  _) = Build(Note("alfa/a.md"), Note("beta/b.md"), Note("gama/c.md"));
        var (second, _) = Build(Note("gama/c.md"), Note("alfa/a.md"), Note("beta/b.md"));

        GraphGrouping.Assign(first);
        GraphGrouping.Assign(second);

        foreach (var node in first)
            Assert.Equal(node.Group, second.Single(n => n.Id == node.Id).Group);
    }

    [Fact]
    public void Adding_a_note_does_not_renumber_the_existing_folders()
    {
        // If it did, the whole vault would change colour every time a file was created.
        var (before, _) = Build(Note("alfa/a.md"), Note("zeta/z.md"));
        var (after,  _) = Build(Note("alfa/a.md"), Note("zeta/z.md"), Note("alfa/a2.md"));

        GraphGrouping.Assign(before);
        GraphGrouping.Assign(after);

        Assert.Equal(
            before.Single(n => n.Id == "zeta/z.md").Group,
            after .Single(n => n.Id == "zeta/z.md").Group);
    }

    [Fact]
    public void A_single_group_anchors_at_the_origin()
    {
        var anchors = GraphGrouping.Anchors(groupCount: 1, nodeCount: 40);

        Assert.Single(anchors);
        Assert.Equal(new GraphPoint(0, 0), anchors[0]);
    }

    [Fact]
    public void No_groups_means_no_anchors()
    {
        Assert.Empty(GraphGrouping.Anchors(groupCount: 0, nodeCount: 0));
    }

    [Fact]
    public void Every_anchor_sits_on_the_same_ring_and_none_share_a_spot()
    {
        var anchors = GraphGrouping.Anchors(groupCount: 6, nodeCount: 300);

        var radii = anchors.Select(a => Math.Sqrt(a.X * a.X + a.Y * a.Y)).ToList();
        foreach (var r in radii) Assert.Equal(radii[0], r, precision: 6);

        Assert.Equal(anchors.Count, anchors.Distinct().Count());
    }

    [Fact]
    public void More_groups_need_a_wider_ring_so_the_anchors_do_not_crowd()
    {
        double few  = Radius(GraphGrouping.Anchors(3,  50));
        double many = Radius(GraphGrouping.Anchors(30, 50));

        Assert.True(many > few, $"30 grupos deberían pedir más radio que 3 ({many} vs {few})");

        static double Radius(IReadOnlyList<GraphPoint> a) =>
            Math.Sqrt(a[0].X * a[0].X + a[0].Y * a[0].Y);
    }

    // ── Spatial (3D) anchors ──

    [Fact]
    public void Flat_anchors_stay_on_the_plane_and_spatial_ones_do_not()
    {
        var flat    = GraphGrouping.Anchors(8, 200, spatial: false);
        var spatial = GraphGrouping.Anchors(8, 200, spatial: true);

        Assert.All(flat, a => Assert.Equal(0, a.Z, precision: 9));
        Assert.Contains(spatial, a => Math.Abs(a.Z) > 1);
    }

    [Fact]
    public void Spatial_anchors_all_sit_on_one_sphere()
    {
        var anchors = GraphGrouping.Anchors(12, 300, spatial: true);

        var radii = anchors.Select(a => Math.Sqrt(a.X * a.X + a.Y * a.Y + a.Z * a.Z)).ToList();
        foreach (var r in radii) Assert.Equal(radii[0], r, precision: 6);
    }

    [Fact]
    public void A_sphere_fits_many_areas_in_less_room_than_a_circle()
    {
        // This is the entire argument for 3D: room grows with r² on a sphere and only with r on a
        // ring, so the same number of folders needs a much smaller radius to stay uncrowded.
        double flat    = Radius(GraphGrouping.Anchors(40, 60, spatial: false));
        double spatial = Radius(GraphGrouping.Anchors(40, 60, spatial: true));

        Assert.True(spatial < flat, $"la esfera debería necesitar menos radio ({spatial} vs {flat})");

        static double Radius(IReadOnlyList<GraphPoint> a) =>
            Math.Sqrt(a[0].X * a[0].X + a[0].Y * a[0].Y + a[0].Z * a[0].Z);
    }

    [Fact]
    public void The_sphere_spiral_spreads_points_instead_of_bunching_them_at_the_poles()
    {
        // Naïve lat/long spacing crowds the poles. Check the points are spread over the full range
        // of heights rather than piled near ±1.
        var points = GraphGrouping.FibonacciSphere(50).ToList();

        Assert.Equal(50, points.Count);
        Assert.All(points, p =>
            Assert.Equal(1, Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z), precision: 6));

        int nearEquator = points.Count(p => Math.Abs(p.Y) < 0.5);
        Assert.True(nearEquator >= 20, $"solo {nearEquator} puntos cerca del ecuador, están apelotonados");
    }
}
