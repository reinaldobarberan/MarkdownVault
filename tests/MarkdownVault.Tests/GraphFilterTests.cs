using MarkdownVault.Services;
using Xunit;
using static MarkdownVault.Tests.GraphTestData;

namespace MarkdownVault.Tests;

/// <summary>
/// Covers <see cref="GraphFilter"/>: which notes survive the link floor, the folder switches, the
/// local-neighbourhood walk and the search box — and, above all, the ORDER those are applied in,
/// since the order changes the answer.
/// </summary>
public class GraphFilterTests
{
    [Fact]
    public void MinDegree_zero_keeps_every_note()
    {
        var (nodes, links) = Build(Note("a.md", "b.md"), Note("b.md"), Note("loose.md"));

        var visible = GraphFilter.VisibleIds(nodes, links, new GraphFilterOptions(MinDegree: 0));

        Assert.Equal(3, visible.Count);
    }

    [Fact]
    public void MinDegree_one_hides_the_notes_with_no_links()
    {
        var (nodes, links) = Build(Note("a.md", "b.md"), Note("b.md"), Note("loose.md"));

        var visible = GraphFilter.VisibleIds(nodes, links, new GraphFilterOptions(MinDegree: 1));

        Assert.Equal(new[] { "a.md", "b.md" }, visible.OrderBy(x => x));
    }

    [Fact]
    public void The_active_note_survives_the_link_floor()
    {
        // Otherwise opening an orphan note would blank the graph instead of showing
        // "this note links nowhere".
        var (nodes, links) = Build(Note("a.md", "b.md"), Note("b.md"), Note("loose.md"));

        var visible = GraphFilter.VisibleIds(nodes, links,
            new GraphFilterOptions(MinDegree: 3, ActiveFile: "loose.md"));

        Assert.Contains("loose.md", visible);
    }

    [Fact]
    public void Search_matches_the_label_ignoring_case()
    {
        var (nodes, links) = Build(Note("Arquitectura.md"), Note("otra.md"));

        var visible = GraphFilter.VisibleIds(nodes, links, new GraphFilterOptions(Search: "arqui"));

        Assert.Equal(new[] { "Arquitectura.md" }, visible.OrderBy(x => x));
    }

    [Fact]
    public void Local_mode_at_depth_one_keeps_only_the_direct_neighbours()
    {
        var (nodes, links) = Build(
            Note("a.md", "b.md"),
            Note("b.md", "c.md"),
            Note("c.md"));

        var visible = GraphFilter.VisibleIds(nodes, links,
            new GraphFilterOptions(LocalGraph: true, ActiveFile: "a.md", LocalDepth: 1));

        Assert.Equal(new[] { "a.md", "b.md" }, visible.OrderBy(x => x));
    }

    [Fact]
    public void Local_mode_at_depth_two_reaches_one_hop_further()
    {
        var (nodes, links) = Build(
            Note("a.md", "b.md"),
            Note("b.md", "c.md"),
            Note("c.md"));

        var visible = GraphFilter.VisibleIds(nodes, links,
            new GraphFilterOptions(LocalGraph: true, ActiveFile: "a.md", LocalDepth: 2));

        Assert.Equal(new[] { "a.md", "b.md", "c.md" }, visible.OrderBy(x => x));
    }

    [Fact]
    public void The_local_walk_cannot_travel_through_a_note_the_link_floor_removed()
    {
        // a—b—c: degrees are 1, 2, 1. A floor of 2 removes a and c; a is the active note so it
        // stays, but c must NOT come back just because it is two hops away — the walk only moves
        // through the subgraph that survived the floor.
        var (nodes, links) = Build(
            Note("a.md", "b.md"),
            Note("b.md", "c.md"),
            Note("c.md"));

        var visible = GraphFilter.VisibleIds(nodes, links, new GraphFilterOptions(
            LocalGraph: true, ActiveFile: "a.md", LocalDepth: 2, MinDegree: 2));

        Assert.Equal(new[] { "a.md", "b.md" }, visible.OrderBy(x => x));
    }

    [Fact]
    public void A_switched_off_folder_disappears()
    {
        var (nodes, links) = Build(
            Note("docs/a.md", "notas/b.md"),
            Note("notas/b.md"));
        GraphGrouping.Assign(nodes);

        int docsGroup = nodes.First(n => n.Id == "docs/a.md").Group;

        var visible = GraphFilter.VisibleIds(nodes, links,
            new GraphFilterOptions(HiddenGroups: new HashSet<int> { docsGroup }));

        Assert.Equal(new[] { "notas/b.md" }, visible.OrderBy(x => x));
    }

    [Fact]
    public void An_empty_graph_filters_to_nothing_without_throwing()
    {
        var visible = GraphFilter.VisibleIds([], [], new GraphFilterOptions(
            Search: "x", LocalGraph: true, ActiveFile: "gone.md", MinDegree: 2));

        Assert.Empty(visible);
    }
}
