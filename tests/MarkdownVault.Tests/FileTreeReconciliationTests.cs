using System.IO;
using MarkdownVault.Services;
using MarkdownVault.ViewModels;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// El explorador refresca una sección RECONCILIANDO el árbol contra el disco, no
/// reemplazándolo. Antes hacía <c>RootNodes[i] = ToNode(BuildTree(root))</c>: nodos nuevos con
/// IsExpanded en false, así que crear un archivo colapsaba todas las carpetas abiertas y —como
/// IsExpanded/IsSelected están bindeados TwoWay contra el nodo— también tiraba la selección y
/// el scroll. Estos tests fijan la identidad de los nodos, que es de donde sale todo eso.
///
/// A propósito NO se registra la raíz en <see cref="FileService"/>: sin AddRoot no hay
/// FileSystemWatcher, y por lo tanto tampoco el <c>Application.Current.Dispatcher</c> que el
/// handler de VaultChanged necesita y que bajo xUnit no existe. La reconciliación se ejercita
/// llamando a RefreshRoot directo.
/// </summary>
public class FileTreeReconciliationTests : IDisposable
{
    private readonly string      _root;
    private readonly FileService _fileService = new();

    public FileTreeReconciliationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"mvtree_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    private FileTreeViewModel LoadedTree()
    {
        var vm = new FileTreeViewModel(_fileService);
        vm.AddRoot(_root);
        return vm;
    }

    private string Dir(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private string File_(string relative, string content = "x")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    private static VaultFileNode Child(VaultFileNode parent, string name) =>
        parent.Children.Single(c => c.Name == name);

    // ─── Lo reportado: crear algo no debe colapsar el árbol ──────────────────

    [Fact]
    public void Refresh_AfterCreatingAFile_LeavesExpandedFoldersExpanded()
    {
        Dir("notas");
        File_("notas/una.md");
        var vm = LoadedTree();

        var notas = Child(vm.RootNodes[0], "notas");
        notas.IsExpanded = true;

        File_("nueva.md");                 // alta en la raíz
        vm.RefreshRoot(_root);

        var notasDespues = Child(vm.RootNodes[0], "notas");
        Assert.Same(notas, notasDespues);  // MISMA instancia → el binding TwoWay sobrevive
        Assert.True(notasDespues.IsExpanded);
        Assert.True(vm.RootNodes[0].IsExpanded);
    }

    [Fact]
    public void Refresh_KeepsExpansionAtEveryDepth()
    {
        File_("a/b/c/hondo.md");
        var vm = LoadedTree();

        var a = Child(vm.RootNodes[0], "a");
        a.IsExpanded = true;
        var b = Child(a, "b");
        b.IsExpanded = true;
        var c = Child(b, "c");
        c.IsExpanded = true;

        File_("a/b/c/otro.md");
        vm.RefreshRoot(_root);

        Assert.True(a.IsExpanded);
        Assert.True(b.IsExpanded);
        Assert.True(c.IsExpanded);
        Assert.Contains(c.Children, n => n.Name == "otro.md");
    }

    [Fact]
    public void Refresh_KeepsTheSelectedNodeSelected()
    {
        File_("elegido.md");
        var vm = LoadedTree();

        var elegido = Child(vm.RootNodes[0], "elegido.md");
        elegido.IsSelected = true;
        vm.SelectedNode    = elegido;

        File_("otro.md");
        vm.RefreshRoot(_root);

        Assert.Same(elegido, vm.SelectedNode);
        Assert.True(elegido.IsSelected);
        Assert.Same(elegido, Child(vm.RootNodes[0], "elegido.md"));
    }

    // ─── Que además refleje el disco de verdad ───────────────────────────────

    [Fact]
    public void Refresh_InsertsNewNodesInDiskOrder_FoldersFirstThenFilesAlphabetically()
    {
        File_("b.md");
        var vm = LoadedTree();

        Dir("zeta");        // carpeta: va PRIMERO aunque su nombre sea el último
        File_("a.md");
        vm.RefreshRoot(_root);

        Assert.Equal(
            new[] { "zeta", "a.md", "b.md" },
            vm.RootNodes[0].Children.Select(n => n.Name).ToArray());
    }

    [Fact]
    public void Refresh_DropsDeletedNodes()
    {
        File_("va.md");
        File_("viene.md");
        var vm = LoadedTree();

        System.IO.File.Delete(Path.Combine(_root, "va.md"));
        vm.RefreshRoot(_root);

        Assert.Equal(new[] { "viene.md" }, vm.RootNodes[0].Children.Select(n => n.Name).ToArray());
    }

    [Fact]
    public void Refresh_DeletedSelection_ClearsSelectedNode()
    {
        File_("condenado.md");
        var vm = LoadedTree();
        vm.SelectedNode = Child(vm.RootNodes[0], "condenado.md");

        System.IO.File.Delete(Path.Combine(_root, "condenado.md"));
        vm.RefreshRoot(_root);

        // Sin esto SelectedNode quedaba apuntando a un nodo huérfano y el próximo "Nuevo
        // archivo" resolvía su carpeta destino contra una ruta que ya no existe.
        Assert.Null(vm.SelectedNode);
    }

    /// <summary>
    /// La clave de identidad incluye si es carpeta: la misma ruta con otro tipo es otro nodo.
    /// Reciclarlo dejaría un archivo dibujado con icono de carpeta y con hijos imposibles.
    /// </summary>
    [Fact]
    public void Refresh_PathThatChangesFromFileToFolder_ReplacesTheNode()
    {
        File_("mutante.md");
        var vm = LoadedTree();
        var antes = Child(vm.RootNodes[0], "mutante.md");
        Assert.False(antes.IsDirectory);

        System.IO.File.Delete(Path.Combine(_root, "mutante.md"));
        Dir("mutante.md");
        vm.RefreshRoot(_root);

        var despues = Child(vm.RootNodes[0], "mutante.md");
        Assert.NotSame(antes, despues);
        Assert.True(despues.IsDirectory);
    }

    [Fact]
    public void Refresh_WithAnActiveSearch_ReappliesTheFilter()
    {
        File_("factura.md");
        File_("receta.md");
        var vm = LoadedTree();
        vm.SearchQuery = "factura";

        Assert.False(Child(vm.RootNodes[0], "receta.md").IsVisible);

        File_("recibo.md");            // nodo nuevo: nace con IsVisible = true
        vm.RefreshRoot(_root);

        Assert.True(Child(vm.RootNodes[0], "factura.md").IsVisible);
        Assert.False(Child(vm.RootNodes[0], "receta.md").IsVisible);
        Assert.False(Child(vm.RootNodes[0], "recibo.md").IsVisible);
    }

    [Fact]
    public void Refresh_OfAnUnknownRoot_IsANoOp()
    {
        File_("solo.md");
        var vm = LoadedTree();

        vm.RefreshRoot(Path.Combine(Path.GetTempPath(), "raiz-que-no-esta-abierta"));

        Assert.Single(vm.RootNodes);
        Assert.Single(vm.RootNodes[0].Children);
    }

    public void Dispose()
    {
        _fileService.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
