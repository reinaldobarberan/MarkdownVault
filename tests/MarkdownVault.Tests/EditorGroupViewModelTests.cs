using System.IO;
using MarkdownVault.Services;
using MarkdownVault.Services.Plugins;
using MarkdownVault.ViewModels;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Split-editor refactor, Phase 0-1: characterizes the dialog seam through
/// <see cref="EditorGroupViewModel"/> (renamed from EditorViewModel in Phase 1, task 1.1).
/// Real FileService against a temp dir, no mocking framework — matches the project's
/// existing test convention (see FileServiceExternalChangeTests.cs).
/// </summary>
public class EditorGroupViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly FileService _fileService = new();
    private readonly PluginRegistry _registry = new();
    private readonly MarkdownService _markdownService;
    private readonly FakeDialogService _dialogService = new();

    public EditorGroupViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"mvedit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _markdownService = new MarkdownService(_registry);
    }

    private EditorGroupViewModel CreateVm(Action<Action>? uiDispatch = null) =>
        new(_fileService, _markdownService, _registry, _dialogService, uiDispatch);

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task CloseTab_DirtyTab_UserCancels_KeepsTabOpen()
    {
        var vm = CreateVm();
        var path = WriteFile("a.md", "hello");
        await vm.OpenFileAsync(path);
        vm.Content = "hello dirty";
        var tab = vm.ActiveTab!;
        _dialogService.ConfirmResult = ConfirmResult.Cancel;

        await vm.CloseTabCommand.ExecuteAsync(tab);

        Assert.Contains(tab, vm.OpenTabs);
        Assert.Equal(1, _dialogService.ConfirmCount);
    }

    [Fact]
    public async Task CloseTab_DirtyTab_UserSaysYes_WritesAndCloses()
    {
        var vm = CreateVm();
        var path = WriteFile("b.md", "hello");
        await vm.OpenFileAsync(path);
        vm.Content = "hello dirty";
        var tab = vm.ActiveTab!;
        _dialogService.ConfirmResult = ConfirmResult.Yes;

        await vm.CloseTabCommand.ExecuteAsync(tab);

        Assert.DoesNotContain(tab, vm.OpenTabs);
        Assert.Equal("hello dirty", File.ReadAllText(path));
    }

    [Fact]
    public async Task CloseTab_DirtyTab_UserSaysNo_DiscardsAndCloses()
    {
        var vm = CreateVm();
        var path = WriteFile("c.md", "hello");
        await vm.OpenFileAsync(path);
        vm.Content = "hello dirty";
        var tab = vm.ActiveTab!;
        _dialogService.ConfirmResult = ConfirmResult.No;

        await vm.CloseTabCommand.ExecuteAsync(tab);

        Assert.DoesNotContain(tab, vm.OpenTabs);
        Assert.Equal("hello", File.ReadAllText(path)); // unchanged — no write occurred
    }

    [Fact]
    public async Task SaveAsync_WriteFails_ShowsError()
    {
        var vm = CreateVm();
        var path = WriteFile("d.md", "hello");
        await vm.OpenFileAsync(path);

        // Force WriteFileAsync to throw deterministically: point CurrentFilePath at a file
        // inside a directory that doesn't exist, so File.WriteAllTextAsync throws
        // DirectoryNotFoundException. No file locking / timing tricks needed.
        vm.CurrentFilePath = Path.Combine(_root, "missing-subdir", "d.md");

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Single(_dialogService.Errors);
    }

    [Fact]
    public async Task SaveAsAsync_UserCancelsPicker_NoWriteOccurs()
    {
        var vm = CreateVm();
        var path = WriteFile("e.md", "hello");
        await vm.OpenFileAsync(path);
        vm.Content = "hello dirty";
        var originalPath = vm.CurrentFilePath;
        _dialogService.SavePathResult = null; // user cancels the picker

        await vm.SaveAsCommand.ExecuteAsync(null);

        Assert.True(vm.IsDirty);
        Assert.Equal(originalPath, vm.CurrentFilePath);
        Assert.Equal("hello", File.ReadAllText(path)); // unchanged on disk
    }

    [Fact]
    public void InsertImage_UserCancelsPicker_NoInsertionRequestedRaised()
    {
        var vm = CreateVm();
        _dialogService.ImagePathResult = null; // user cancels the picker
        var raised = false;
        vm.InsertionRequested += (_, _) => raised = true;

        vm.InsertImageCommand.Execute(null);

        Assert.False(raised);
    }

    [Fact]
    public void InsertInternalLink_EmptyVault_ShowsInfoMessage()
    {
        var vm = CreateVm(); // no vault opened -> GetAllVaultFiles() returns []

        vm.InsertInternalLinkCommand.Execute(null);

        Assert.Single(_dialogService.Infos);
    }

    /// <summary>
    /// Regression test for the multi-vault change: InsertInternalLink resolves
    /// GetOwningRoot(CurrentFilePath) and calls FileService.GetVaultFiles(owningRoot) to build
    /// the link-picker candidate list. With two vaults open at once, a file in vault A must only
    /// ever be offered notes from vault A — never vault B's — and vice versa. Captures the
    /// candidate list the VM hands to IDialogService.PickInternalLinkMarkdown (via
    /// FakeDialogService.LastVaultFiles) rather than driving the real LinkPickerDialog.
    /// </summary>
    [Fact]
    public async Task InsertInternalLink_TwoVaultsOpen_CandidatesScopedToOwningVault()
    {
        var vaultA = Path.Combine(Path.GetTempPath(), $"mvedit_vaultA_{Guid.NewGuid():N}");
        var vaultB = Path.Combine(Path.GetTempPath(), $"mvedit_vaultB_{Guid.NewGuid():N}");
        Directory.CreateDirectory(vaultA);
        Directory.CreateDirectory(vaultB);

        try
        {
            var alphaPath = Path.Combine(vaultA, "AlphaNote.md");
            var betaPath  = Path.Combine(vaultB, "BetaNote.md");
            File.WriteAllText(alphaPath, "alpha");
            File.WriteAllText(betaPath, "beta");

            _fileService.AddRoot(vaultA);
            _fileService.AddRoot(vaultB);

            var vm = CreateVm();

            // Active tab is in vault A -> candidates must be vault A's notes only.
            await vm.OpenFileAsync(alphaPath);
            vm.InsertInternalLinkCommand.Execute(null);

            Assert.NotNull(_dialogService.LastVaultFiles);
            Assert.Contains("AlphaNote.md", _dialogService.LastVaultFiles!);
            Assert.DoesNotContain("BetaNote.md", _dialogService.LastVaultFiles!);

            // Active tab moves to vault B -> candidates must flip to vault B's notes only.
            await vm.OpenFileAsync(betaPath);
            vm.InsertInternalLinkCommand.Execute(null);

            Assert.NotNull(_dialogService.LastVaultFiles);
            Assert.Contains("BetaNote.md", _dialogService.LastVaultFiles!);
            Assert.DoesNotContain("AlphaNote.md", _dialogService.LastVaultFiles!);
        }
        finally
        {
            _fileService.RemoveRoot(vaultA);
            _fileService.RemoveRoot(vaultB);
            try { Directory.Delete(vaultA, recursive: true); } catch { /* best effort */ }
            try { Directory.Delete(vaultB, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void HandleDroppedFiles_InvalidFile_ShowsError()
    {
        var vm = CreateVm(); // no vault open, no file open -> CopyImageToAssets throws

        vm.HandleDroppedFiles(new[] { "photo.png" });

        Assert.Single(_dialogService.Errors);
    }

    // NOTE: HandleExternalChange_CleanTab_ReloadsFromDisk / _DirtyTab_KeepsInAppVersion moved
    // to WorkbenchInvariantTests.cs (Phase 3, task 3.8) — the external-change subscription
    // relocated from this group to MainViewModel (design §5.5: single workbench-level lookup
    // instead of every group subscribing separately), so the entry point they exercise is now
    // MainViewModel, not EditorGroupViewModel directly. Mechanical relocation, same policy,
    // same assertions.

    public void Dispose()
    {
        _fileService.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
