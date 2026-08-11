using System.IO;
using MarkdownVault.Services;
using Xunit;

namespace MarkdownVault.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _path;

    public SettingsServiceTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"mvset_{Guid.NewGuid():N}.json");
    }

    [Fact]
    public void Load_missing_file_returns_defaults()
    {
        var settings = new SettingsService(_path).Load();

        Assert.NotNull(settings);
        Assert.Empty(settings.PluginsEnabled);
    }

    [Fact]
    public void PluginsEnabled_round_trips_through_disk()
    {
        var service  = new SettingsService(_path);
        var settings = service.Load();
        settings.PluginsEnabled["core.mermaid"] = false;
        service.Save(settings);

        var reloaded = new SettingsService(_path).Load();

        Assert.True(reloaded.PluginsEnabled.TryGetValue("core.mermaid", out var value));
        Assert.False(value);
    }

    [Fact]
    public void KnownVaultPaths_defaults_to_empty()
    {
        var settings = new SettingsService(_path).Load();

        Assert.Empty(settings.KnownVaultPaths);
    }

    [Fact]
    public void KnownVaultPaths_round_trips_through_disk_preserving_order()
    {
        var service  = new SettingsService(_path);
        var settings = service.Load();
        settings.KnownVaultPaths.Add(@"C:\Vaults\Work");
        settings.KnownVaultPaths.Add(@"C:\Vaults\Personal");
        service.Save(settings);

        var reloaded = new SettingsService(_path).Load();

        Assert.Equal(
            new[] { @"C:\Vaults\Work", @"C:\Vaults\Personal" },
            reloaded.KnownVaultPaths);
    }

    [Fact]
    public void IsSplit_defaults_to_false_and_SplitRatio_defaults_to_half()
    {
        var settings = new SettingsService(_path).Load();

        Assert.False(settings.IsSplit);
        Assert.Equal(0.5, settings.SplitRatio);
    }

    [Fact]
    public void IsSplit_and_SplitRatio_round_trip_through_disk()
    {
        // Regression guard against design C8's failure mode: FileTreeWidth/EditorColumnWidth
        // are declared in AppSettings but never actually read or written anywhere. SplitRatio
        // must not join that graveyard — both ends (ctor read, SaveSettings write) are wired.
        var service  = new SettingsService(_path);
        var settings = service.Load();
        settings.IsSplit    = true;
        settings.SplitRatio = 0.35;
        service.Save(settings);

        var reloaded = new SettingsService(_path).Load();

        Assert.True(reloaded.IsSplit);
        Assert.Equal(0.35, reloaded.SplitRatio);
    }

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { /* best effort */ }
    }
}
