using MarkdownVault.Models;
using MarkdownVault.Services;
using Xunit;
using static MarkdownVault.Tests.GraphTestData;

namespace MarkdownVault.Tests;

/// <summary>
/// Covers <see cref="GraphMetrics"/>: how big each note is drawn.
///
/// The property that matters is that the size is RELATIVE to the vault. An absolute formula has to
/// assume a range of link counts, and vaults disagree wildly about that — a loose pile of notes
/// tops out around five links while a dense wiki reaches thirty. Tuned for the first, the second
/// draws nodes that swallow the space between them, and zooming out does not help because
/// everything shrinks together and the PROPORTION is what is wrong.
/// </summary>
public class GraphMetricsTests
{
    [Fact]
    public void A_note_nothing_links_to_gets_the_smallest_radius()
    {
        var (nodes, _) = Build(Note("a.md", "b.md"), Note("b.md"), Note("suelta.md"));

        GraphMetrics.Assign(nodes);

        Assert.Equal(GraphMetrics.MinRadius, nodes.Single(n => n.Id == "suelta.md").Radius, precision: 9);
    }

    [Fact]
    public void The_busiest_note_in_the_vault_gets_the_largest_radius()
    {
        var (nodes, _) = Build(
            Note("hub.md", "a.md", "b.md", "c.md"),
            Note("a.md"), Note("b.md"), Note("c.md"));

        GraphMetrics.Assign(nodes);

        Assert.Equal(GraphMetrics.MaxRadius, nodes.Single(n => n.Id == "hub.md").Radius, precision: 9);
    }

    [Fact]
    public void No_note_ever_draws_outside_the_range()
    {
        var (nodes, _) = Build(
            Note("hub.md", "a.md", "b.md", "c.md", "d.md"),
            Note("a.md", "b.md"), Note("b.md"), Note("c.md"), Note("d.md"), Note("suelta.md"));

        GraphMetrics.Assign(nodes);

        Assert.All(nodes, n =>
        {
            Assert.True(n.Radius >= GraphMetrics.MinRadius, $"{n.Id} quedó bajo el mínimo: {n.Radius}");
            Assert.True(n.Radius <= GraphMetrics.MaxRadius, $"{n.Id} se pasó del máximo: {n.Radius}");
        });
    }

    [Fact]
    public void A_dense_wiki_draws_no_bigger_than_a_sparse_vault()
    {
        // The whole point. Both vaults are one hub plus its leaves; the dense one simply has ten
        // times the links. The BIGGEST node has to come out the same either way, or the dense
        // vault turns into a blob while the sparse one looks fine.
        var sparse = Chain(hubLinks: 3);
        var dense  = Chain(hubLinks: 30);

        GraphMetrics.Assign(sparse);
        GraphMetrics.Assign(dense);

        Assert.Equal(sparse.Max(n => n.Radius), dense.Max(n => n.Radius), precision: 9);

        // The smallest does NOT have to match, and should not: a one-link note among thirty is
        // genuinely less important than a one-link note among three, and the size says so. What
        // matters is that it never leaves the range.
        Assert.True(dense.Min(n => n.Radius) >= GraphMetrics.MinRadius);
        Assert.True(dense.Min(n => n.Radius) < sparse.Min(n => n.Radius),
            "en un vault denso una hoja debería verse relativamente más chica");
    }

    [Fact]
    public void More_links_still_means_a_bigger_node()
    {
        // Normalising must not flatten the encoding — that is the information the size carries.
        var (nodes, _) = Build(
            Note("big.md", "a.md", "b.md", "c.md", "d.md"),
            Note("mid.md", "a.md", "b.md"),
            Note("a.md"), Note("b.md"), Note("c.md"), Note("d.md"));

        GraphMetrics.Assign(nodes);

        var big = nodes.Single(n => n.Id == "big.md");
        var mid = nodes.Single(n => n.Id == "mid.md");
        var leaf = nodes.Single(n => n.Id == "c.md");

        Assert.True(big.Radius > mid.Radius, $"{big.Radius} debería superar a {mid.Radius}");
        Assert.True(mid.Radius > leaf.Radius, $"{mid.Radius} debería superar a {leaf.Radius}");
    }

    [Fact]
    public void The_largest_node_stays_well_under_the_spring_length()
    {
        // Springs settle neighbours 130 apart. A hub whose diameter approached that would leave no
        // visible gap between linked notes, which is exactly the solid-blob look.
        Assert.True(GraphMetrics.MaxRadius * 2 < 130 / 3.0,
            $"diámetro máximo {GraphMetrics.MaxRadius * 2} es demasiado para enlaces de 130");
    }

    [Fact]
    public void A_vault_where_nothing_links_to_anything_is_all_minimum_sized()
    {
        var (nodes, _) = Build(Note("a.md"), Note("b.md"), Note("c.md"));

        GraphMetrics.Assign(nodes);

        Assert.All(nodes, n => Assert.Equal(GraphMetrics.MinRadius, n.Radius, precision: 9));
    }

    [Fact]
    public void An_empty_vault_does_not_throw()
    {
        GraphMetrics.Assign([]);
    }

    // ── The user's size dial ──

    [Fact]
    public void The_size_dial_scales_every_node_by_the_same_factor()
    {
        var (nodes, _) = Build(Note("hub.md", "a.md", "b.md"), Note("a.md"), Note("b.md"));

        GraphMetrics.Assign(nodes);
        var normal = nodes.ToDictionary(n => n.Id, n => n.Radius);

        GraphMetrics.Assign(nodes, 2.0);

        Assert.All(nodes, n => Assert.Equal(normal[n.Id] * 2, n.Radius, precision: 9));
    }

    [Fact]
    public void The_dial_is_clamped_so_nodes_can_never_vanish_or_swallow_the_graph()
    {
        var (nodes, _) = Build(Note("hub.md", "a.md"), Note("a.md"));

        GraphMetrics.Assign(nodes, 0.0001);
        double tiny = nodes.Max(n => n.Radius);

        GraphMetrics.Assign(nodes, 500);
        double huge = nodes.Max(n => n.Radius);

        Assert.Equal(GraphMetrics.MaxRadius * GraphMetrics.MinScale, tiny, precision: 9);
        Assert.Equal(GraphMetrics.MaxRadius * GraphMetrics.MaxScale, huge, precision: 9);
    }

    [Fact]
    public void Re_applying_the_dial_does_not_compound()
    {
        // The radius is recomputed from the degree every time, not multiplied into whatever was
        // there before — otherwise dragging the slider would grow the nodes without bound.
        var (nodes, _) = Build(Note("hub.md", "a.md"), Note("a.md"));

        GraphMetrics.Assign(nodes, 1.5);
        double once = nodes.Max(n => n.Radius);

        GraphMetrics.Assign(nodes, 1.5);
        GraphMetrics.Assign(nodes, 1.5);

        Assert.Equal(once, nodes.Max(n => n.Radius), precision: 9);
    }

    [Fact]
    public void The_dial_keeps_the_relative_ordering_intact()
    {
        var (nodes, _) = Build(
            Note("big.md", "a.md", "b.md", "c.md"),
            Note("mid.md", "a.md"),
            Note("a.md"), Note("b.md"), Note("c.md"));

        GraphMetrics.Assign(nodes, 0.4);

        var big = nodes.Single(n => n.Id == "big.md").Radius;
        var mid = nodes.Single(n => n.Id == "mid.md").Radius;
        Assert.True(big > mid, "achicar todo no debería aplanar la diferencia entre un hub y una hoja");
    }

    /// <summary>One hub linked to <paramref name="hubLinks"/> leaves.</summary>
    private static List<GraphNode> Chain(int hubLinks)
    {
        var leaves = Enumerable.Range(0, hubLinks).Select(i => $"leaf{i:00}.md").ToArray();
        var spec = new List<(string, string[])> { Note("hub.md", leaves) };
        spec.AddRange(leaves.Select(Note));
        var (nodes, _) = Build(spec.ToArray());
        return nodes;
    }
}
