using MarkdownVault.Services.Plugins;
using Xunit;

namespace MarkdownVault.Tests;

public class PluginManifestTests
{
    [Fact]
    public void Valid_manifest_passes()
    {
        var m = new PluginManifest { Id = "a.b", Name = "N", Version = "1.0.0", Entry = "e.dll" };

        Assert.True(m.IsValid(out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("",    "N", "1.0.0", "e.dll", "id")]
    [InlineData("a.b", "",  "1.0.0", "e.dll", "name")]
    [InlineData("a.b", "N", "",      "e.dll", "version")]
    [InlineData("a.b", "N", "1.0.0", "",      "entry")]
    public void Missing_required_field_fails_with_specific_error(
        string id, string name, string version, string entry, string expectedFragment)
    {
        var m = new PluginManifest { Id = id, Name = name, Version = version, Entry = entry };

        Assert.False(m.IsValid(out var error));
        Assert.Contains(expectedFragment, error!, System.StringComparison.OrdinalIgnoreCase);
    }
}
