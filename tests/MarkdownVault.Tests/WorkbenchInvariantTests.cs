using System.IO;
using MarkdownVault.Services;
using MarkdownVault.Services.Plugins;
using MarkdownVault.ViewModels;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Split-editor refactor, Phase 3: the path-uniqueness invariant (one path → exactly one
/// group → one buffer → one auto-save timer) is the highest-value surface in the whole
/// change — it structurally eliminates the divergent-buffer / double-auto-save risks the
/// exploration flagged as CRITICAL. This is the ONE non-negotiable test file this batch
/// keeps real coverage for, per the mode-change instruction.
///
/// The split UI doesn't exist until Phase 4, so — per design's own testability note — these
/// tests exercise the invariant by adding a second <see cref="EditorGroupViewModel"/> to
/// <see cref="MainViewModel.Groups"/> directly (the VM layer is unit-testable independent of
/// the View). Real FileService against a temp dir, no mocking framework — matches the
/// project's existing test convention.
/// </summary>
public class WorkbenchInvariantTests : IDisposable
{
    private readonly string _root;
    private readonly FileService _fileService = new();
    private readonly PluginRegistry _registry = new();
    private readonly MarkdownService _markdownService;
    private readonly FakeDialogService _dialogService = new();

    public WorkbenchInvariantTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"mvwb_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _markdownService = new MarkdownService(_registry);
    }

    private MainViewModel CreateVm() =>
        new(_fileService, _markdownService,
            new SettingsService(Path.Combine(_root, "settings.json")),
            _registry, _dialogService, uiDispatch: a => a());

    /// <summary>Second pane, added directly per the testability note above — Phase 4 wires the real UI.</summary>
    private EditorGroupViewModel AddSecondGroup(MainViewModel vm)
    {
        var groupB = new EditorGroupViewModel(_fileService, _markdownService, _registry, _dialogService);
        vm.Groups.Add(groupB);
        return groupB;
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task FindOwner_ReturnsGroupOwningPath()
    {
        var vm     = CreateVm();
        var groupB = AddSecondGroup(vm);
        var path   = WriteFile("a.md", "hello");

        await groupB.OpenFileAsync(path);

        Assert.Same(groupB, vm.FindOwner(path));
        Assert.Null(vm.FindOwner(Path.Combine(_root, "nowhere.md")));
    }

    [Fact]
    public async Task OpenInGroupAsync_AlreadyOpenElsewhere_FocusesOwnerInsteadOfDuplicating()
    {
        var vm     = CreateVm();
        var groupA = vm.Groups[0];
        var groupB = AddSecondGroup(vm);
        var path   = WriteFile("b.md", "hello");
        await groupB.OpenFileAsync(path);

        // Ask group A to open a path already owned by group B.
        await vm.OpenInGroupAsync(groupA, path);

        Assert.Empty(groupA.OpenTabs);                 // no duplicate created in A
        Assert.Same(groupB, vm.FocusedGroup);           // B was focused instead
        Assert.Equal(path, groupB.ActiveTab?.FilePath); // and switched to the existing tab
    }

    [Fact]
    public async Task SaveAsAsync_RekeysOwnerLookup_FindOwnerReturnsNewPathNotOld()
    {
        // Regression test for bug #273: before OpenTab.FilePath became settable, Save-As left
        // the tab registered under its OLD path, so FindOwner(oldPath) still matched and
        // FindOwner(newPath) would have created a duplicate on the next open.
        var vm      = CreateVm();
        var oldPath = WriteFile("c.md", "hello");
        var newPath = Path.Combine(_root, "c-renamed.md");
        await vm.OpenInFocusedGroupAsync(oldPath);
        _dialogService.SavePathResult = newPath;

        await vm.FocusedGroup.SaveAsCommand.ExecuteAsync(null);

        Assert.Null(vm.FindOwner(oldPath));
        Assert.Same(vm.FocusedGroup, vm.FindOwner(newPath));
    }

    [Fact]
    public async Task MoveTabToOtherGroup_PreservesContentDirtyCaretScroll()
    {
        var vm     = CreateVm();
        var groupA = vm.Groups[0];
        var groupB = AddSecondGroup(vm);
        var path   = WriteFile("d.md", "original");
        await groupA.OpenFileAsync(path);
        var tab = groupA.ActiveTab!;
        groupA.Content     = "dirty edit";   // mirrors into tab.Content + tab.IsDirty
        tab.ScrollOffset   = 42;
        tab.CaretOffset    = 7;

        vm.MoveTabToOtherGroup(tab);

        Assert.DoesNotContain(tab, groupA.OpenTabs);
        Assert.Contains(tab, groupB.OpenTabs);
        Assert.Same(tab, groupB.ActiveTab);
        Assert.Same(groupB, vm.FocusedGroup);
        Assert.Equal("dirty edit", tab.Content);
        Assert.True(tab.IsDirty);
        Assert.Equal(42, tab.ScrollOffset);
        Assert.Equal(7, tab.CaretOffset);
    }

    [Fact]
    public async Task HandleExternalChange_NotifiesOnlyOwningGroup()
    {
        var vm     = CreateVm();
        var groupB = AddSecondGroup(vm);
        var pathA  = WriteFile("e.md", "original a");
        var pathB  = WriteFile("f.md", "original b");
        _fileService.OpenVault(_root); // starts the real FileSystemWatcher
        await vm.OpenInFocusedGroupAsync(pathA);
        await groupB.OpenFileAsync(pathB);
        var tabA = vm.Groups[0].ActiveTab!;
        var tabB = groupB.ActiveTab!;

        // External edit to A's file only.
        File.WriteAllText(pathA, "changed externally");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (tabA.Content != "changed externally" && DateTime.UtcNow < deadline)
            await Task.Delay(50);

        Assert.Equal("changed externally", tabA.Content);
        Assert.Equal("original b", tabB.Content); // B was never touched
    }

    // ─── Relocated from EditorGroupViewModelTests.cs (task 3.8) ────────────────
    // HandleExternalChange's dirty/clean policy moved from the group to MainViewModel
    // (design §5.5 — a single workbench-level subscription instead of every group
    // subscribing separately). Same policy, same assertions, new entry point.

    [Fact]
    public async Task HandleExternalChange_CleanTab_ReloadsFromDisk()
    {
        var vm   = CreateVm();
        var path = WriteFile("g.md", "original");
        _fileService.OpenVault(_root);
        await vm.OpenInFocusedGroupAsync(path);
        var tab = vm.FocusedGroup.ActiveTab!;
        Assert.False(tab.IsDirty);

        File.WriteAllText(path, "changed externally");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (tab.Content != "changed externally" && DateTime.UtcNow < deadline)
            await Task.Delay(50);

        Assert.Equal("changed externally", tab.Content);
        Assert.False(tab.IsDirty);
    }

    [Fact]
    public async Task HandleExternalChange_DirtyTab_KeepsInAppVersion()
    {
        var vm   = CreateVm();
        var path = WriteFile("h.md", "original");
        _fileService.OpenVault(_root);
        await vm.OpenInFocusedGroupAsync(path);
        vm.FocusedGroup.Content = "in-app edit";
        var tab = vm.FocusedGroup.ActiveTab!;
        Assert.True(tab.IsDirty);

        File.WriteAllText(path, "changed externally");

        // Policy: a dirty tab's in-app version always wins, silently.
        await Task.Delay(1000);

        Assert.Equal("in-app edit", tab.Content);
        Assert.True(tab.IsDirty);
    }

    // ─── Phase 4: manual unsplit migration (SE-2) ──────────────────────────────────────────
    // Explicitly called out as worth a real test — it touches data safety (a layout gesture
    // must never discard a dirty tab's unsaved content).

    [Fact]
    public async Task ExitSplit_MigratesPaneBTabsIntoA_PreservesDirtyState_AndKeepsA_ActiveTab()
    {
        var vm = CreateVm();
        vm.EnterSplit();
        var groupA = vm.Groups[0];
        var groupB = vm.Groups[1];

        var pathA  = WriteFile("i.md", "a content");
        var pathB1 = WriteFile("j.md", "b1 content");
        var pathB2 = WriteFile("k.md", "b2 content");

        await groupA.OpenFileAsync(pathA);
        await groupB.OpenFileAsync(pathB1);
        await groupB.OpenFileAsync(pathB2);          // pathB2 becomes B's active tab
        groupB.Content = "dirty edit";                // mirrors into the active tab (pathB2)

        vm.ExitSplit();

        Assert.False(vm.IsSplit);
        Assert.Single(vm.Groups);
        Assert.Equal(3, groupA.OpenTabs.Count);
        Assert.Equal(pathA, groupA.ActiveTab?.FilePath);   // A's own previously-active tab wins, not B's
        Assert.Same(groupA, vm.FocusedGroup);

        var migratedDirtyTab = groupA.OpenTabs.Single(t => t.FilePath == pathB2);
        Assert.True(migratedDirtyTab.IsDirty);
        Assert.Equal("dirty edit", migratedDirtyTab.Content);
        var migratedCleanTab = groupA.OpenTabs.Single(t => t.FilePath == pathB1);
        Assert.False(migratedCleanTab.IsDirty);

        // Exactly one tab reads as active in the merged single strip.
        Assert.Equal(1, groupA.OpenTabs.Count(t => t.IsActive));
    }

    [Fact]
    public async Task ExitSplit_PaneAEmpty_PromotesPaneBsActiveTab_InsteadOfLeavingNoneActive()
    {
        // Edge case not spelled out verbatim in the spec's scenarios (which assume A has an
        // active tab): A's last tab was closed while split was active (SE-5 — A stays visible,
        // does not auto-collapse), then the user manually unsplits. The merged strip must not
        // end up with tabs but no active one.
        var vm = CreateVm();
        vm.EnterSplit();
        var groupA = vm.Groups[0];
        var groupB = vm.Groups[1];
        var pathB  = WriteFile("m.md", "hello");
        await groupB.OpenFileAsync(pathB);
        Assert.Null(groupA.ActiveTab);

        vm.ExitSplit();

        Assert.Equal(pathB, groupA.ActiveTab?.FilePath);
        Assert.True(groupA.ActiveTab!.IsActive);
    }

    public void Dispose()
    {
        _fileService.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
