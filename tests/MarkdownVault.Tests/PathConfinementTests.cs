using System.IO;
using MarkdownVault.Services.Plugins;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Cubre el helper compartido de confinamiento de rutas (<see cref="PathConfinement"/>),
/// usado tanto por <c>HostServices.ReadFileAsync</c> (vault) como por el futuro
/// <c>PluginStorage</c> (sandbox por plugin).
/// </summary>
public class PathConfinementTests : IDisposable
{
    private readonly string _root;

    public PathConfinementTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"mvconf_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Valid_relative_path_resolves_under_root()
    {
        var full = PathConfinement.ResolveWithin(_root, "notes.md");

        Assert.Equal(Path.Combine(_root, "notes.md"), full);
    }

    [Fact]
    public void Valid_nested_relative_path_resolves_under_root()
    {
        var full = PathConfinement.ResolveWithin(_root, Path.Combine("sub", "notes.md"));

        Assert.Equal(Path.Combine(_root, "sub", "notes.md"), full);
    }

    [Fact]
    public void Parent_traversal_is_rejected()
    {
        Assert.Throws<UnauthorizedAccessException>(
            () => PathConfinement.ResolveWithin(_root, ".."));
    }

    [Fact]
    public void Deep_parent_traversal_is_rejected()
    {
        Assert.Throws<UnauthorizedAccessException>(
            () => PathConfinement.ResolveWithin(_root, Path.Combine("..", "..", "other")));
    }

    [Fact]
    public void Absolute_windows_path_is_rejected()
    {
        Assert.Throws<UnauthorizedAccessException>(
            () => PathConfinement.ResolveWithin(_root, @"C:\Windows\System32\config"));
    }

    [Fact]
    public void Rooted_path_is_rejected()
    {
        Assert.Throws<UnauthorizedAccessException>(
            () => PathConfinement.ResolveWithin(_root, Path.DirectorySeparatorChar + "etc" + Path.DirectorySeparatorChar + "passwd"));
    }

    [Fact]
    public void Sibling_prefix_bypass_is_rejected()
    {
        // "PluginData\a" y "PluginData\ab" comparten prefijo de texto pero "ab" NO está
        // dentro de "a\". El check de prefijo con separador final debe distinguirlos.
        var rootA = Path.Combine(_root, "a");
        Directory.CreateDirectory(_root + "ab");

        Assert.Throws<UnauthorizedAccessException>(
            () => PathConfinement.ResolveWithin(rootA, Path.Combine("..", "ab", "secret.txt")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_root + "ab", recursive: true); } catch { /* best effort */ }
    }
}
