using System.IO;
using MarkdownVault.PluginSdk;
using MarkdownVault.Services.Plugins;
using Xunit;

namespace MarkdownVault.Tests;

public class PluginRegistryTests
{
    private static PreviewAsset Asset(string value) =>
        new() { Kind = AssetKind.Script, Source = AssetSource.Inline, Value = value };

    [Fact]
    public void PreviewAssets_are_hidden_until_owner_is_enabled()
    {
        var r = new PluginRegistry();
        r.AddPreviewAsset("p1", Asset("a"));

        Assert.Empty(r.PreviewAssets);          // sin set de habilitados → nada visible

        r.SetEnabled("p1", true);
        var only = Assert.Single(r.PreviewAssets);
        Assert.Equal("a", only.Value);
    }

    [Fact]
    public void SetEnabled_false_hides_owner_assets()
    {
        var r = new PluginRegistry();
        r.AddPreviewAsset("p1", Asset("a"));
        r.SetEnabled("p1", true);

        r.SetEnabled("p1", false);

        Assert.Empty(r.PreviewAssets);
    }

    [Fact]
    public void Only_enabled_owners_contributions_are_returned()
    {
        var r = new PluginRegistry();
        r.AddPreviewAsset("p1", Asset("a"));
        r.AddPreviewAsset("p2", Asset("b"));

        r.SetEnabledSet(new[] { "p1" });

        var only = Assert.Single(r.PreviewAssets);
        Assert.Equal("a", only.Value);
    }

    [Fact]
    public void MarkdownContributions_are_ordered_by_order_and_filtered_by_enabled()
    {
        var r = new PluginRegistry();
        var high = new FakeMarkdownContribution();
        var low  = new FakeMarkdownContribution();
        r.AddMarkdownContribution("p1", high, order: 10);
        r.AddMarkdownContribution("p1", low,  order: 1);
        r.AddMarkdownContribution("p2", new FakeMarkdownContribution(), order: 0);

        r.SetEnabled("p1", true);   // p2 queda deshabilitado

        var list = r.MarkdownContributions.ToList();
        Assert.Equal(new IMarkdownContribution[] { low, high }, list);
    }

    [Fact]
    public void Clear_removes_everything_including_enabled_set()
    {
        var r = new PluginRegistry();
        r.AddPreviewAsset("p1", Asset("a"));
        r.SetEnabled("p1", true);

        r.Clear();

        Assert.Empty(r.PreviewAssets);
        Assert.False(r.IsEnabled("p1"));
    }

    [Fact]
    public void RaiseChanged_invokes_subscribers()
    {
        var r = new PluginRegistry();
        var count = 0;
        r.Changed += () => count++;

        r.RaiseChanged();

        Assert.Equal(1, count);
    }

    [Fact]
    public void IsEnabled_is_case_insensitive()
    {
        var r = new PluginRegistry();
        r.SetEnabled("Core.Mermaid", true);

        Assert.True(r.IsEnabled("core.mermaid"));
    }

    [Fact]
    public void RequestPreviewRefresh_via_HostPluginContext_fires_Changed_once()
    {
        var r = new PluginRegistry();
        var count = 0;
        r.Changed += () => count++;

        var storage = new PluginStorage(Path.Combine(Path.GetTempPath(), $"mvtest_{Guid.NewGuid():N}"));
        var ctx = new HostPluginContext(
            new PluginMetadata { Id = "test.plugin" },
            new FakeHost(),
            r,
            baseDir: "",
            storage: storage);

        ctx.RequestPreviewRefresh();

        Assert.Equal(1, count);
    }
}
