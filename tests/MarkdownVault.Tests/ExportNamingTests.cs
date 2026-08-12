using MarkdownVault.Services;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Covers the default-name rule shared by the PNG and PDF export dialogs. The rule was
/// extracted out of the WebView2 export handlers (which can't run headless) precisely so
/// it could be unit-tested here in isolation.
/// </summary>
public class ExportNamingTests
{
    [Fact]
    public void DefaultFileName_uses_file_stem_when_path_present()
    {
        Assert.Equal("nota", ExportNaming.DefaultFileName(@"C:\vault\nota.md"));
    }

    [Fact]
    public void DefaultFileName_strips_only_the_extension()
    {
        Assert.Equal("mi.nota", ExportNaming.DefaultFileName(@"C:\vault\mi.nota.md"));
    }

    [Fact]
    public void DefaultFileName_handles_bare_file_name_without_directory()
    {
        Assert.Equal("readme", ExportNaming.DefaultFileName("readme.markdown"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DefaultFileName_falls_back_when_no_meaningful_path(string? path)
    {
        Assert.Equal(ExportNaming.Fallback, ExportNaming.DefaultFileName(path));
    }
}
