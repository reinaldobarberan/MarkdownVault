using MarkdownVault.Helpers;
using Xunit;

namespace MarkdownVault.Tests;

public class FileNodeIconConverterTests
{
    // Segoe MDL2 Assets code points under test.
    private const string FolderClosed = ""; // Folder
    private const string FolderOpen   = ""; // OpenFolderHorizontal
    private const string Document     = ""; // Document
    private const string Photo        = ""; // Photo2
    private const string Code         = ""; // Code

    // ── Folders: open vs closed ───────────────────────────────────────────────

    [Fact]
    public void Collapsed_folder_uses_closed_folder_glyph()
    {
        Assert.Equal(FolderClosed,
            FileNodeToIconConverter.GlyphFor(isDirectory: true, isExpanded: false, name: "notes"));
    }

    [Fact]
    public void Expanded_folder_uses_open_folder_glyph()
    {
        Assert.Equal(FolderOpen,
            FileNodeToIconConverter.GlyphFor(isDirectory: true, isExpanded: true, name: "notes"));
    }

    // ── Files by extension ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Nota.md")]
    [InlineData("README.markdown")]
    [InlineData("plano.txt")]
    [InlineData("archivo-sin-extension")]
    public void Markdown_and_text_use_document_glyph(string name)
    {
        Assert.Equal(Document,
            FileNodeToIconConverter.GlyphFor(isDirectory: false, isExpanded: false, name: name));
    }

    [Theory]
    [InlineData("foto.png")]
    [InlineData("Diagrama.JPG")]  // case-insensitive
    [InlineData("icon.svg")]
    [InlineData("captura.webp")]
    public void Images_use_photo_glyph(string name)
    {
        Assert.Equal(Photo,
            FileNodeToIconConverter.GlyphFor(isDirectory: false, isExpanded: false, name: name));
    }

    [Theory]
    [InlineData("config.json")]
    [InlineData("data.YAML")]
    [InlineData("page.html")]
    [InlineData("style.css")]
    public void Data_and_markup_use_code_glyph(string name)
    {
        Assert.Equal(Code,
            FileNodeToIconConverter.GlyphFor(isDirectory: false, isExpanded: false, name: name));
    }

    // ── Convert() plumbing ────────────────────────────────────────────────────

    [Fact]
    public void Convert_reads_values_in_order_isDirectory_isExpanded_name()
    {
        var converter = new FileNodeToIconConverter();

        var result = converter.Convert(
            new object[] { false, false, "foto.png" },
            typeof(string), parameter: null!, culture: null!);

        Assert.Equal(Photo, result);
    }

    [Fact]
    public void Convert_tolerates_missing_values()
    {
        var converter = new FileNodeToIconConverter();

        // No values at all → treated as a non-directory file with no name → Document.
        var result = converter.Convert(
            System.Array.Empty<object>(),
            typeof(string), parameter: null!, culture: null!);

        Assert.Equal(Document, result);
    }
}
