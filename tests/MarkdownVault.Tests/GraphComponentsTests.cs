using MarkdownVault.Services;
using Xunit;
using static MarkdownVault.Tests.GraphTestData;

namespace MarkdownVault.Tests;

/// <summary>
/// Covers <see cref="GraphComponents"/>: finding the islands of notes that can reach each other by
/// following links, keeping the link-less notes out of that count, and numbering the islands the
/// same way every time so the layout does not jump between rebuilds.
/// </summary>
public class GraphComponentsTests
{
    [Fact]
    public void A_chain_of_notes_is_one_island()
    {
        var (nodes, links) = Build(
            Note("a.md", "b.md"),
            Note("b.md", "c.md"),
            Note("c.md"));

        int islands = GraphComponents.Assign(nodes, links);

        Assert.Equal(1, islands);
        Assert.Single(nodes.Select(n => n.Component).Distinct());
    }

    [Fact]
    public void Two_unconnected_clusters_are_two_islands()
    {
        var (nodes, links) = Build(
            Note("a.md", "b.md"),
            Note("b.md"),
            Note("x.md", "y.md"),
            Note("y.md"));

        int islands = GraphComponents.Assign(nodes, links);

        Assert.Equal(2, islands);
        Assert.Equal(
            nodes.Single(n => n.Id == "a.md").Component,
            nodes.Single(n => n.Id == "b.md").Component);
        Assert.NotEqual(
            nodes.Single(n => n.Id == "a.md").Component,
            nodes.Single(n => n.Id == "x.md").Component);
    }

    [Fact]
    public void Notes_with_no_links_are_marked_orphans_not_islands_of_one()
    {
        // They are laid out by hand rather than simulated, so they must be distinguishable.
        var (nodes, links) = Build(
            Note("a.md", "b.md"),
            Note("b.md"),
            Note("suelta1.md"),
            Note("suelta2.md"));

        int islands = GraphComponents.Assign(nodes, links);

        Assert.Equal(1, islands);
        Assert.Equal(GraphComponents.Orphan, nodes.Single(n => n.Id == "suelta1.md").Component);
        Assert.Equal(GraphComponents.Orphan, nodes.Single(n => n.Id == "suelta2.md").Component);
    }

    [Fact]
    public void Island_numbers_do_not_depend_on_the_order_the_notes_arrive_in()
    {
        var (first, firstLinks) = Build(
            Note("a.md", "b.md"), Note("b.md"),
            Note("x.md", "y.md"), Note("y.md"));

        var (second, secondLinks) = Build(
            Note("y.md"), Note("x.md", "y.md"),
            Note("b.md"), Note("a.md", "b.md"));

        GraphComponents.Assign(first, firstLinks);
        GraphComponents.Assign(second, secondLinks);

        foreach (var node in first)
            Assert.Equal(node.Component, second.Single(n => n.Id == node.Id).Component);
    }

    [Fact]
    public void A_cycle_is_still_a_single_island()
    {
        var (nodes, links) = Build(
            Note("a.md", "b.md"),
            Note("b.md", "c.md"),
            Note("c.md", "a.md"));

        Assert.Equal(1, GraphComponents.Assign(nodes, links));
    }

    [Fact]
    public void An_empty_graph_has_no_islands()
    {
        Assert.Equal(0, GraphComponents.Assign([], []));
    }
}
