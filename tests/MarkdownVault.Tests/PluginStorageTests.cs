using System.IO;
using MarkdownVault.PluginSdk;
using MarkdownVault.Services.Plugins;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Cubre <see cref="PluginStorage"/>: implementación host de <see cref="IPluginStorage"/>,
/// el almacenamiento sandbox por plugin bajo un root inyectable (aquí, un directorio
/// temporal — en producción, <c>PluginData/&lt;plugin-id&gt;/</c> vía <c>PluginManager</c>).
/// </summary>
public class PluginStorageTests : IDisposable
{
    private readonly string _root;
    private readonly PluginStorage _storage;

    public PluginStorageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"mvstorage_{Guid.NewGuid():N}");
        _storage = new PluginStorage(_root);
    }

    [Fact]
    public async Task Write_then_read_returns_exact_content()
    {
        await _storage.WriteTextAsync("notes.md", "hello world");

        var result = await _storage.ReadTextAsync("notes.md");

        Assert.Equal("hello world", result);
    }

    [Fact]
    public async Task Write_overwrites_existing_content_instead_of_appending()
    {
        await _storage.WriteTextAsync("notes.md", "first version");
        await _storage.WriteTextAsync("notes.md", "second version");

        var result = await _storage.ReadTextAsync("notes.md");

        Assert.Equal("second version", result);
    }

    [Fact]
    public async Task Read_missing_file_throws_instead_of_returning_null_or_empty()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _storage.ReadTextAsync("never-written.md"));
    }

    [Fact]
    public async Task Delete_removes_an_existing_file()
    {
        await _storage.WriteTextAsync("notes.md", "content");

        _storage.Delete("notes.md");

        Assert.False(_storage.Exists("notes.md"));
    }

    [Fact]
    public void Delete_of_already_missing_file_does_not_throw()
    {
        var ex = Record.Exception(() => _storage.Delete("never-existed.md"));

        Assert.Null(ex);
    }

    [Fact]
    public void Root_is_not_created_just_by_constructing_or_checking_existence()
    {
        _storage.Exists("notes.md");

        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task First_write_creates_the_sandbox_root_lazily()
    {
        Assert.False(Directory.Exists(_root));

        await _storage.WriteTextAsync("notes.md", "content");

        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public async Task Write_creates_intermediate_subdirectories()
    {
        var rel = Path.Combine("sub", "deep", "notes.md");

        await _storage.WriteTextAsync(rel, "content");

        Assert.True(File.Exists(Path.Combine(_root, "sub", "deep", "notes.md")));
    }

    [Fact]
    public async Task Write_does_not_emit_a_utf8_byte_order_mark()
    {
        await _storage.WriteTextAsync("notes.md", "content");

        var bytes = await File.ReadAllBytesAsync(Path.Combine(_root, "notes.md"));
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

        Assert.False(hasBom);
    }

    [Fact]
    public async Task Write_with_parent_traversal_is_rejected_before_any_io()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _storage.WriteTextAsync(Path.Combine("..", "secret.txt"), "x"));

        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task Write_with_absolute_path_is_rejected()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _storage.WriteTextAsync(@"C:\Windows\System32\evil.txt", "x"));
    }

    [Fact]
    public async Task Write_with_cross_plugin_traversal_is_rejected()
    {
        // Simula un intento de escapar hacia la carpeta de OTRO plugin, hermana de la propia.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _storage.WriteTextAsync(Path.Combine("..", "other-plugin", "tasks.json"), "x"));
    }

    [Fact]
    public async Task Read_with_parent_traversal_is_rejected()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _storage.ReadTextAsync(Path.Combine("..", "secret.txt")));
    }

    [Fact]
    public void Exists_with_parent_traversal_is_rejected()
    {
        Assert.Throws<UnauthorizedAccessException>(
            () => _storage.Exists(Path.Combine("..", "secret.txt")));
    }

    [Fact]
    public void Delete_with_parent_traversal_is_rejected()
    {
        Assert.Throws<UnauthorizedAccessException>(
            () => _storage.Delete(Path.Combine("..", "secret.txt")));
    }

    [Fact]
    public void RootPath_exposes_the_configured_sandbox_root()
    {
        Assert.Equal(Path.GetFullPath(_root), Path.GetFullPath(_storage.RootPath));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
