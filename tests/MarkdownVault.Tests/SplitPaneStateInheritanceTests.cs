using System.IO;
using MarkdownVault.Services;
using MarkdownVault.Services.Plugins;
using MarkdownVault.ViewModels;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Regresión: el pane B nacía SIN heredar el estado del workbench, así que al dividir la
/// pantalla en tema oscuro su preview se renderizaba en claro (fondo blanco). El WebView2 es
/// único y sigue al FocusedGroup (SE-8), por lo que basta enfocar B para ver el bug.
/// <see cref="MainViewModel.CreateGroup"/> es el ÚNICO punto de nacimiento de un grupo, y es
/// ahí donde ahora se hereda el estado — estos tests cubren las dos rutas que lo crean:
/// el gesto de dividir (<see cref="MainViewModel.EnterSplit"/>) y el restore desde settings.
/// </summary>
public class SplitPaneStateInheritanceTests : IDisposable
{
    private readonly string          _root;
    private readonly FileService     _fileService = new();
    private readonly PluginRegistry  _registry    = new();
    private readonly MarkdownService _markdownService;
    private readonly FakeDialogService _dialogService = new();

    public SplitPaneStateInheritanceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"mvsplit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _markdownService = new MarkdownService(_registry);
    }

    private string SettingsPath => Path.Combine(_root, "settings.json");

    private MainViewModel CreateVm() =>
        new(_fileService, _markdownService, new SettingsService(SettingsPath),
            _registry, _dialogService, uiDispatch: a => a());

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void EnterSplit_PaneB_InheritsDarkTheme()
    {
        var vm = CreateVm();
        vm.IsDarkTheme = true;

        vm.EnterSplit();

        Assert.True(vm.Groups[1].IsDarkTheme);
    }

    [Fact]
    public async Task EnterSplit_PaneB_RendersPreviewInDark()
    {
        // El síntoma real que reportó el usuario: el panel dividido salía blanco. El marcador
        // es la clase "dark" del body que agrega MarkdownService.RenderToHtml.
        var vm = CreateVm();
        vm.IsDarkTheme = true;
        vm.EnterSplit();
        var groupB = vm.Groups[1];

        await groupB.OpenFileAsync(WriteFile("b.md", "# hola"));
        groupB.RefreshPreviewFromPlugins();   // fuerza el render sin esperar el debounce

        Assert.Contains("markdown-body dark", groupB.PreviewHtml);
    }

    [Fact]
    public void RestoredSplit_PaneB_InheritsDarkTheme()
    {
        // El restore de la geometría del split ocurre DESPUÉS del bloque "Apply persisted
        // settings" del ctor, así que el loop que propagaba el tema ya había pasado.
        var service  = new SettingsService(SettingsPath);
        var settings = service.Load();
        settings.IsDarkTheme = true;
        settings.IsSplit     = true;
        service.Save(settings);

        var vm = CreateVm();

        Assert.True(vm.IsSplit);
        Assert.Equal(2, vm.Groups.Count);
        Assert.True(vm.Groups[1].IsDarkTheme);
    }

    [Fact]
    public void ToggleTheme_WhileSplit_ReachesBothPanes()
    {
        var vm = CreateVm();
        vm.EnterSplit();

        vm.IsDarkTheme = true;

        Assert.All(vm.Groups, g => Assert.True(g.IsDarkTheme));
    }

    public void Dispose()
    {
        _fileService.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
