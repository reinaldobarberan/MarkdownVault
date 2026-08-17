using System.IO;
using MarkdownVault.Services;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Covers <see cref="GraphService.BuildAsync(string)"/>'s vault scoping (Phase 6, multi-vault
/// change): the graph built for one root must never contain nodes or edges from another open
/// vault, even when both vaults contain notes with matching titles (vault-scoped-resolution
/// spec, "Graph Scoped To Focused Vault" / "No cross-vault edges appear").
/// </summary>
public class GraphServiceTests : IDisposable
{
    private readonly string      _baseDir;
    private readonly string      _rootA;
    private readonly string      _rootB;
    private readonly FileService _fileService = new();
    private readonly GraphService _graph;

    public GraphServiceTests()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), $"mvgraph_{Guid.NewGuid():N}");
        _rootA   = Path.Combine(_baseDir, "VaultA");
        _rootB   = Path.Combine(_baseDir, "VaultB");
        Directory.CreateDirectory(_rootA);
        Directory.CreateDirectory(_rootB);
        _graph = new GraphService(_fileService);
    }

    [Fact]
    public async Task BuildAsync_scopes_nodes_to_the_given_root_only()
    {
        File.WriteAllText(Path.Combine(_rootA, "A.md"), "# A");
        File.WriteAllText(Path.Combine(_rootB, "B.md"), "# B");
        _fileService.AddRoot(_rootA);
        _fileService.AddRoot(_rootB);

        var data = await _graph.BuildAsync(Path.GetFullPath(_rootA));

        Assert.Single(data.Nodes);
        Assert.Equal("A", data.Nodes[0].Label);
    }

    [Fact]
    public async Task BuildAsync_yields_no_cross_vault_edges_for_matching_titles()
    {
        // Both vaults have a "Home" note linking to a "Target" note also present in both —
        // a same-title pair that WOULD wrongly link across vaults if scoping were broken
        // (byName resolution is title-based, not path-based).
        File.WriteAllText(Path.Combine(_rootA, "Home.md"), "[[Target]]");
        File.WriteAllText(Path.Combine(_rootA, "Target.md"), "# A's Target");
        File.WriteAllText(Path.Combine(_rootB, "Home.md"), "[[Target]]");
        File.WriteAllText(Path.Combine(_rootB, "Target.md"), "# B's Target");

        _fileService.AddRoot(_rootA);
        _fileService.AddRoot(_rootB);

        var rootAFull = Path.GetFullPath(_rootA);
        var dataA = await _graph.BuildAsync(rootAFull);

        Assert.Equal(2, dataA.Nodes.Count);
        Assert.All(dataA.Nodes, n =>
            Assert.StartsWith(rootAFull, n.FullPath, StringComparison.OrdinalIgnoreCase));

        Assert.Single(dataA.Links);
        Assert.All(dataA.Links, l =>
        {
            Assert.StartsWith(rootAFull, l.Source.FullPath, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(rootAFull, l.Target.FullPath, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task BuildAsync_with_no_files_in_root_returns_empty_graph()
    {
        var data = await _graph.BuildAsync(Path.GetFullPath(_rootA));

        Assert.Empty(data.Nodes);
        Assert.Empty(data.Links);
    }

    public void Dispose()
    {
        _fileService.Dispose();
        try { Directory.Delete(_baseDir, recursive: true); } catch { /* best effort */ }
    }
}
