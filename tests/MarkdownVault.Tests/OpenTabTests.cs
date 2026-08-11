using MarkdownVault.Models;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Split-editor refactor, Phase 3 (task 3.1-3.2): <see cref="OpenTab.FilePath"/> became
/// settable so Save-As can re-key the tab's identity (bug #273) — the path-uniqueness
/// invariant's Owns()/Find() lookups key on this field.
/// </summary>
public class OpenTabTests
{
    [Fact]
    public void FilePath_IsSettable_AndRaisesFileNameExtensionDisplayName()
    {
        var tab = new OpenTab(@"C:\vault\old.md");
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.FilePath = @"C:\vault\renamed.md";

        Assert.Equal(@"C:\vault\renamed.md", tab.FilePath);
        Assert.Equal("renamed.md", tab.FileName);
        Assert.Equal(".md", tab.Extension);
        Assert.Contains(nameof(OpenTab.FileName), raised);
        Assert.Contains(nameof(OpenTab.Extension), raised);
        Assert.Contains(nameof(OpenTab.DisplayName), raised);
    }
}
