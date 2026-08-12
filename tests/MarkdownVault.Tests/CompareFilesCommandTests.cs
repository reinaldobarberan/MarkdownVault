using System.IO;
using MarkdownVault.Services;
using MarkdownVault.Services.Plugins;
using MarkdownVault.ViewModels;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Cubre "Comparar archivos" de punta a punta en la capa VM: gating del comando, apertura
/// de la vista con el HTML del diff, y el merge bidireccional (◀/▶) aplicado al buffer en
/// memoria del panel destino con re-render. Usa un doble de <see cref="ICompareView"/> en
/// lugar de la Window WebView2 real (no construible bajo xUnit headless).
/// </summary>
public class CompareFilesCommandTests : IDisposable
{
    private readonly string _root;
    private readonly FileService _fileService = new();
    private readonly PluginRegistry _registry = new();
    private readonly MarkdownService _markdownService;
    private readonly FakeDialogService _dialogService = new();
    private readonly FakeCompareView _view = new();

    public CompareFilesCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"mvcmp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _markdownService = new MarkdownService(_registry);
    }

    private MainViewModel CreateVm()
    {
        var vm = new MainViewModel(_fileService, _markdownService,
            new SettingsService(Path.Combine(_root, "settings.json")),
            _registry, _dialogService, uiDispatch: a => a());
        vm.CompareViewFactory = () => _view;
        return vm;
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private async Task<MainViewModel> SplitWith(string aContent, string bContent)
    {
        var vm = CreateVm();
        vm.EnterSplit();
        await vm.Groups[0].OpenFileAsync(WriteFile("a.md", aContent));
        await vm.Groups[1].OpenFileAsync(WriteFile("b.md", bContent));
        return vm;
    }

    [Fact]
    public void CannotCompare_when_not_split()
    {
        var vm = CreateVm();
        Assert.False(vm.CompareFilesCommand.CanExecute(null));
    }

    [Fact]
    public async Task CannotCompare_when_split_but_one_pane_empty()
    {
        var vm = CreateVm();
        vm.EnterSplit();
        await vm.Groups[0].OpenFileAsync(WriteFile("a.md", "solo A"));

        Assert.False(vm.CompareFilesCommand.CanExecute(null));
    }

    [Fact]
    public async Task CanCompare_once_both_panes_have_a_file()
    {
        var vm = await SplitWith("linea 1\nlinea 2", "linea 1\nlinea DOS");
        Assert.True(vm.CompareFilesCommand.CanExecute(null));
    }

    [Fact]
    public async Task Executing_shows_the_view_with_diff_html_and_titled()
    {
        var vm = await SplitWith("hello world", "hello there");

        vm.CompareFilesCommand.Execute(null);

        Assert.NotNull(_view.ShownHtml);
        Assert.Contains("<!DOCTYPE html>", _view.ShownHtml);
        Assert.Contains("class=\"row mod\"", _view.ShownHtml);
        Assert.Contains("a.md", _view.ShownTitle);
        Assert.Contains("b.md", _view.ShownTitle);
    }

    [Fact]
    public async Task Compares_in_memory_content_including_unsaved_edits()
    {
        var vm = await SplitWith("original", "original");
        vm.Groups[1].Content = "editado sin guardar";   // edición no guardada en panel B

        vm.CompareFilesCommand.Execute(null);

        Assert.Contains("class=\"row mod\"", _view.ShownHtml);
    }

    [Fact]
    public async Task MergeToRight_makes_right_match_left_and_marks_it_dirty()
    {
        // row0 igual, row1 modificada ("dos" vs "DOS").
        var vm = await SplitWith("uno\ndos", "uno\nDOS");
        vm.CompareFilesCommand.Execute(null);

        _view.RaiseMerge(1, MergeDirection.ToRight);

        Assert.Equal("uno\ndos", vm.Groups[1].Content);
        Assert.True(vm.Groups[1].IsDirty);
        Assert.True(_view.ReloadCount >= 1);       // la vista se refrescó tras el merge
    }

    [Fact]
    public async Task MergeToLeft_makes_left_match_right()
    {
        var vm = await SplitWith("uno\ndos", "uno\nDOS");
        vm.CompareFilesCommand.Execute(null);

        _view.RaiseMerge(1, MergeDirection.ToLeft);

        Assert.Equal("uno\nDOS", vm.Groups[0].Content);
    }

    [Fact]
    public async Task MergeToRight_on_deleted_line_inserts_it_into_the_right()
    {
        // A tiene una línea "b" que B no tiene → row1 es "deleted" (solo izquierda).
        var vm = await SplitWith("a\nb\nc", "a\nc");
        vm.CompareFilesCommand.Execute(null);

        _view.RaiseMerge(1, MergeDirection.ToRight);

        Assert.Equal("a\nb\nc", vm.Groups[1].Content);
    }

    [Fact]
    public async Task BlockMerge_copies_the_whole_contiguous_block_at_once()
    {
        // A tiene dos líneas seguidas que B no tiene → bloque "deleted" de 2 filas (índices 1 y 2).
        var vm = await SplitWith("a\nb\nc\nd", "a\nd");
        vm.CompareFilesCommand.Execute(null);

        _view.RaiseMerge(1, MergeDirection.ToRight, block: true);

        Assert.Equal("a\nb\nc\nd", vm.Groups[1].Content);
    }

    [Fact]
    public async Task Closing_the_view_ends_the_session()
    {
        var vm = await SplitWith("uno\ndos", "uno\nDOS");
        vm.CompareFilesCommand.Execute(null);
        _view.RaiseClosed();

        // Tras cerrar, un merge tardío no debe tocar los buffers.
        _view.RaiseMerge(1, MergeDirection.ToRight);
        Assert.Equal("uno\nDOS", vm.Groups[1].Content);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    /// <summary>Doble en memoria de la superficie de comparación.</summary>
    private sealed class FakeCompareView : ICompareView
    {
        public event Action<CompareMergeRequest>? MergeRequested;
        public event Action?                       ViewClosed;

        public string? ShownHtml;
        public string? ShownTitle;
        public string? LastReloadHtml;
        public int      ReloadCount;

        public void Show(string html, bool isDark, string title) { ShownHtml = html; ShownTitle = title; }
        public void Reload(string html) { LastReloadHtml = html; ReloadCount++; }

        public void RaiseMerge(int row, MergeDirection dir, bool block = false) =>
            MergeRequested?.Invoke(new CompareMergeRequest(row, dir, block));
        public void RaiseClosed() => ViewClosed?.Invoke();
    }
}
