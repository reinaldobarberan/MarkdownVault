using System.IO;
using System.Linq;
using MarkdownVault.Models;
using MarkdownVault.Services;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Covers source-code file support: the file tree lists code files alongside notes,
/// while the graph / internal-link surfaces (<see cref="FileService.GetAllVaultFiles"/>)
/// stay note-only — a code file is viewable but never a wikilink target.
/// </summary>
public class SupportedExtensionsTests : IDisposable
{
    private readonly string      _root;
    private readonly FileService _svc = new();

    public SupportedExtensionsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"mvcode_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    private void Touch(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    // ─── Classification helpers ──────────────────────────────────────────────

    [Theory]
    [InlineData("a.md")]
    [InlineData("a.mermaid")]
    [InlineData("a.html")]
    public void Note_extensions_are_notes(string name) =>
        Assert.True(SupportedExtensions.IsNote(name));

    [Theory]
    [InlineData("a.cs")]
    [InlineData("a.js")]
    [InlineData("a.py")]
    public void Code_extensions_are_code_not_notes(string name)
    {
        Assert.True(SupportedExtensions.IsCode(name));
        Assert.False(SupportedExtensions.IsNote(name));
    }

    [Fact]
    public void LanguageFor_maps_code_to_highlight_id()
    {
        Assert.Equal("csharp", SupportedExtensions.LanguageFor("Program.cs"));
        Assert.Equal("python", SupportedExtensions.LanguageFor("run.py"));
        Assert.Null(SupportedExtensions.LanguageFor("note.md"));
    }

    // ─── Tree building ───────────────────────────────────────────────────────

    [Fact]
    public void Tree_lists_both_notes_and_code_but_not_binaries()
    {
        Touch("note.md");
        Touch("Program.cs");
        Touch("script.js");
        Touch("run.py");
        Touch("photo.png");   // image → not viewable
        Touch("notes.txt");   // plain text → not viewable

        var names = _svc.BuildTree(_root).Children
            .Where(c => !c.IsDirectory)
            .Select(c => c.Name)
            .ToList();

        Assert.Contains("note.md", names);
        Assert.Contains("Program.cs", names);
        Assert.Contains("script.js", names);
        Assert.Contains("run.py", names);
        Assert.DoesNotContain("photo.png", names);
        Assert.DoesNotContain("notes.txt", names);
    }

    // ─── Link/graph surface stays note-only ──────────────────────────────────

    [Fact]
    public void GetAllVaultFiles_excludes_code_files()
    {
        _svc.OpenVault(_root);
        Touch("note.md");
        Touch("Program.cs");

        var files = _svc.GetAllVaultFiles();

        Assert.Contains("note.md", files);
        Assert.DoesNotContain("Program.cs", files);
    }

    // ─── File creation ───────────────────────────────────────────────────────

    [Fact]
    public void CreateFile_keeps_a_code_extension()
    {
        var path = _svc.CreateFile(_root, "script.js");
        Assert.Equal("script.js", Path.GetFileName(path));
    }

    [Fact]
    public void CreateFile_appends_md_to_a_bare_name()
    {
        var path = _svc.CreateFile(_root, "MyNote");
        Assert.Equal("MyNote.md", Path.GetFileName(path));
    }

    public void Dispose()
    {
        _svc.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
