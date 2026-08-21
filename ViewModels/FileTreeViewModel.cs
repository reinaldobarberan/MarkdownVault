using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkdownVault.Services;

namespace MarkdownVault.ViewModels;

// ─── Node ────────────────────────────────────────────────────────────────────

/// <summary>Observable tree node for the file explorer panel.</summary>
public partial class VaultFileNode : ObservableObject
{
    [ObservableProperty] private bool   _isExpanded;
    [ObservableProperty] private bool   _isSelected;
    [ObservableProperty] private bool   _isVisible = true;

    public string          Name        { get; init; } = string.Empty;
    public string          FullPath    { get; init; } = string.Empty;
    public bool            IsDirectory { get; init; }
    public VaultFileNode?  Parent      { get; init; }

    public ObservableCollection<VaultFileNode> Children { get; } = new();
}

// ─── ViewModel ───────────────────────────────────────────────────────────────

/// <summary>Drives the left-panel file explorer TreeView.</summary>
public partial class FileTreeViewModel : ObservableObject
{
    private readonly FileService _fileService;

    public FileTreeViewModel(FileService fileService)
    {
        _fileService = fileService;

        // Scoped refresh (multi-root): the watcher tells us WHICH root changed, so only
        // that section rebuilds instead of every open vault's tree.
        _fileService.VaultChanged += (_, change) =>
            Application.Current.Dispatcher.Invoke(() => RefreshRoot(change.Root));
    }

    // ─── Properties ──────────────────────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<VaultFileNode> _rootNodes = new();

    [ObservableProperty]
    private VaultFileNode? _selectedNode;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>Raised when a file should be opened in the editor.</summary>
    public event Action<string>? FileOpenRequested;

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Legacy single-root entry point, preserved for callers not yet migrated to
    /// <see cref="AddRoot"/>/<see cref="RemoveRoot"/> (Phase 5): replaces every open
    /// section with just <paramref name="path"/>, matching the pre-multi-vault
    /// "switch vault" behavior.
    /// </summary>
    public void LoadVault(string path)
    {
        RootNodes.Clear();
        AddRoot(path);
    }

    /// <summary>
    /// Adds <paramref name="path"/> as a new top-level section in <see cref="RootNodes"/>,
    /// built fresh from disk. Idempotent: a no-op (no duplicate section) when the root is
    /// already open — mirrors <see cref="FileService.AddRoot"/>'s dedup so the two stay
    /// in sync. Mutates <see cref="RootNodes"/> in place; every other open root's section
    /// is left untouched.
    /// </summary>
    public void AddRoot(string path)
    {
        var normalized = Path.GetFullPath(path);
        if (RootNodes.Any(r => string.Equals(r.FullPath, normalized, StringComparison.OrdinalIgnoreCase)))
            return;

        var tree = _fileService.BuildTree(normalized);
        var node = ToNode(tree, null);
        node.IsExpanded = true;
        RootNodes.Add(node);
    }

    /// <summary>
    /// Removes <paramref name="path"/>'s section from <see cref="RootNodes"/>, if present.
    /// Idempotent: a no-op when the root isn't open. Does not touch any open editor tab —
    /// closing a vault leaves its tabs open per design (only the sidebar section and the
    /// underlying <see cref="FileService"/> watcher go away).
    /// </summary>
    public void RemoveRoot(string path)
    {
        var normalized = Path.GetFullPath(path);
        var match = RootNodes.FirstOrDefault(
            r => string.Equals(r.FullPath, normalized, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            RootNodes.Remove(match);
    }

    /// <summary>
    /// Selects the node matching <paramref name="fullPath"/> in the tree, expanding its parent
    /// folders so it is visible. No-op if the path is not present in the current tree. Used to
    /// reveal the target of an internal link in the file explorer.
    /// </summary>
    public void RevealFile(string fullPath)
    {
        var node = RootNodes.Select(r => FindNode(r, fullPath)).FirstOrDefault(n => n is not null);
        if (node is null) return;

        // Expand every ancestor folder so the container is realized and visible.
        for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
            ancestor.IsExpanded = true;

        // TreeView is single-select: setting IsSelected on the target clears the previous one
        // through its TwoWay binding.
        node.IsSelected = true;
        SelectedNode   = node;
    }

    private static VaultFileNode? FindNode(VaultFileNode node, string fullPath)
    {
        if (string.Equals(node.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
            return node;

        foreach (var child in node.Children)
            if (FindNode(child, fullPath) is { } match)
                return match;

        return null;
    }

    // ─── Commands ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenFile(VaultFileNode? node)
    {
        if (node is { IsDirectory: false })
            FileOpenRequested?.Invoke(node.FullPath);
    }

    [RelayCommand]
    private void CreateFile()
    {
        var dir = TargetDirectory();
        if (dir is null)
        {
            MessageBox.Show("Abre un vault primero (Archivo → Abrir vault).",
                "Sin vault abierto", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var name = InputDialog.Prompt("Nuevo archivo",
            $"Nombre del archivo (en {TargetDisplayName(dir)}):", "NuevoArchivo.md");
        if (string.IsNullOrWhiteSpace(name)) return;

        var path = _fileService.CreateFile(dir, name);
        RefreshOwnerOf(dir);
        ExpandDirectory(dir);
        FileOpenRequested?.Invoke(path);
    }

    [RelayCommand]
    private void CreateFolder()
    {
        var dir = TargetDirectory();
        if (dir is null)
        {
            MessageBox.Show("Abre un vault primero (Archivo → Abrir vault).",
                "Sin vault abierto", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var name = InputDialog.Prompt("Nueva carpeta",
            $"Nombre de la carpeta (en {TargetDisplayName(dir)}):", "Nueva carpeta");
        if (string.IsNullOrWhiteSpace(name)) return;

        _fileService.CreateDirectory(dir, name);
        RefreshOwnerOf(dir);
        ExpandDirectory(dir);
    }

    [RelayCommand]
    private void RenameNode(VaultFileNode? node)
    {
        if (node is null) return;

        var newName = InputDialog.Prompt("Renombrar", "Nuevo nombre:", node.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == node.Name) return;

        // Root doesn't change on a rename (same directory, new name), so the pre-rename
        // path still resolves to the right owning root for the post-rename refresh.
        var owningRoot = _fileService.GetOwningRoot(node.FullPath);
        _fileService.Rename(node.FullPath, newName);
        if (owningRoot is not null) RefreshRoot(owningRoot);
    }

    [RelayCommand]
    private void DeleteNode(VaultFileNode? node)
    {
        if (node is null) return;

        var msg    = node.IsDirectory ? $"¿Eliminar la carpeta '{node.Name}' y todo su contenido?" : $"¿Eliminar '{node.Name}'?";
        var result = MessageBox.Show(msg, "Confirmar eliminación",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        // GetOwningRoot is a string-prefix match, not a disk check, so it still resolves
        // correctly after Delete removes the path.
        var owningRoot = _fileService.GetOwningRoot(node.FullPath);
        _fileService.Delete(node.FullPath);
        if (owningRoot is not null) RefreshRoot(owningRoot);
    }

    // ─── Search ──────────────────────────────────────────────────────────────

    partial void OnSearchQueryChanged(string value)
    {
        foreach (var root in RootNodes)
            ApplyFilter(root, value.Trim().ToLowerInvariant());
    }

    private static bool ApplyFilter(VaultFileNode node, string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            node.IsVisible = true;
            foreach (var c in node.Children) ApplyFilter(c, query);
            return true;
        }

        bool selfMatch     = node.Name.ToLowerInvariant().Contains(query);
        bool childrenMatch = false;

        foreach (var child in node.Children)
            childrenMatch |= ApplyFilter(child, query);

        node.IsVisible  = selfMatch || childrenMatch;
        if (childrenMatch) node.IsExpanded = true;
        return node.IsVisible;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Sincroniza una sección con el disco — usado tanto por el handler acotado de
    /// <see cref="FileService.VaultChanged"/> como por los comandos CRUD de arriba, así que un
    /// alta/renombre/borrado local refresca solo el vault afectado y nunca todos los abiertos.
    /// Una raíz obsoleta (ya cerrada vía <see cref="RemoveRoot"/>, p. ej. una notificación
    /// tardía del watcher) se ignora en silencio.
    ///
    /// RECONCILIA el árbol existente en vez de reemplazarlo (ver <see cref="Reconcile"/>). Antes
    /// hacía <c>RootNodes[index] = ToNode(BuildTree(root))</c>, y eso fabricaba nodos nuevos con
    /// <see cref="VaultFileNode.IsExpanded"/> en false: crear un archivo colapsaba TODAS las
    /// carpetas abiertas, porque de la expansión solo se rescataba la de la raíz.
    ///
    /// Interna, no privada, para que los tests puedan ejercitar la reconciliación sin pasar por
    /// los comandos (que abren diálogos modales) ni por el watcher (que necesita Dispatcher).
    /// </summary>
    internal void RefreshRoot(string root)
    {
        var index = -1;
        for (var i = 0; i < RootNodes.Count; i++)
        {
            if (string.Equals(RootNodes[i].FullPath, root, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }
        if (index < 0) return;

        Reconcile(RootNodes[index], _fileService.BuildTree(root));

        // La selección pudo haberse borrado del disco (comando Eliminar, o un borrado externo).
        // Dejarla apuntando a un nodo que ya no cuelga del árbol deja la TreeView sin nada
        // marcado y a TargetDirectory() resolviendo contra una ruta muerta: el próximo "Nuevo
        // archivo" iría a una carpeta que no existe.
        if (SelectedNode is { } selected &&
            RootNodes.All(r => FindNode(r, selected.FullPath) is null))
        {
            SelectedNode = null;
        }

        // Los nodos recién insertados nacen con IsVisible = true. Sin esto, crear un archivo con
        // el buscador activo destapa el árbol entero como si no hubiera filtro.
        var query = SearchQuery.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(query))
            ApplyFilter(RootNodes[index], query);
    }

    /// <summary>
    /// Hace que <paramref name="node"/> refleje a <paramref name="file"/> MUTANDO su lista de
    /// hijos: reutiliza el nodo que ya existe para cada ruta que sigue estando, y solo crea los
    /// que aparecieron.
    ///
    /// Preservar la INSTANCIA es todo el punto. <c>IsExpanded</c> e <c>IsSelected</c> están
    /// bindeados TwoWay contra el nodo desde el ItemContainerStyle de la TreeView
    /// (FileTreeView.xaml), así que mientras el objeto sobreviva sobreviven también la carpeta
    /// abierta, la fila seleccionada y la posición del scroll. Reemplazar la sección entera
    /// tiraba las tres cosas de una.
    ///
    /// La identidad es (ruta + si es carpeta). El tipo entra en la clave a propósito: borrar un
    /// archivo y crear una carpeta con el mismo nombre debe dar un nodo NUEVO, no reciclar uno
    /// que la plantilla dibujaría con el icono equivocado.
    ///
    /// Un renombre sí pierde el estado de esa rama: cambia la ruta, así que para la clave es un
    /// nodo distinto. Es correcto — adivinar que dos rutas distintas "son el mismo" pide una
    /// heurística que se equivocaría en los casos que importan.
    /// </summary>
    private static void Reconcile(VaultFileNode node, Models.VaultFile file)
    {
        var desired     = file.Children;
        var desiredKeys = desired.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 1. Fuera los que ya no están en disco. De atrás hacia adelante: borrar por índice no
        //    corre de lugar a los que todavía no se miraron.
        for (var i = node.Children.Count - 1; i >= 0; i--)
            if (!desiredKeys.Contains(Key(node.Children[i])))
                node.Children.RemoveAt(i);

        // 2. Recorrer el orden de disco dejando cada hijo en su posición. Tras el paso 1 todo
        //    lo que queda existe, y las posiciones 0..i-1 ya están bien, así que el que falta
        //    en la posición i solo puede estar de i en adelante.
        for (var i = 0; i < desired.Count; i++)
        {
            var key = Key(desired[i]);

            if (i < node.Children.Count && KeyEquals(node.Children[i], key))
            {
                Reconcile(node.Children[i], desired[i]);
                continue;
            }

            var existing = IndexOfKey(node.Children, key, from: i);
            if (existing >= 0)
            {
                node.Children.Move(existing, i);
                Reconcile(node.Children[i], desired[i]);
            }
            else
            {
                node.Children.Insert(i, ToNode(desired[i], node));
            }
        }
    }

    // El prefijo distingue carpeta de archivo para la MISMA ruta. Sin él, un archivo borrado y
    // una carpeta creada con su nombre se tomarían por el mismo nodo.
    private static string Key(VaultFileNode node)      => (node.IsDirectory ? "D:" : "F:") + node.FullPath;
    private static string Key(Models.VaultFile file)   => (file.IsDirectory ? "D:" : "F:") + file.FullPath;

    private static bool KeyEquals(VaultFileNode node, string key) =>
        string.Equals(Key(node), key, StringComparison.OrdinalIgnoreCase);

    private static int IndexOfKey(IList<VaultFileNode> children, string key, int from)
    {
        for (var i = from; i < children.Count; i++)
            if (KeyEquals(children[i], key))
                return i;
        return -1;
    }

    /// <summary>Refreshes whichever open root owns <paramref name="path"/>, if any.</summary>
    private void RefreshOwnerOf(string path)
    {
        if (_fileService.GetOwningRoot(path) is { } root)
            RefreshRoot(root);
    }

    /// <summary>
    /// Abre la carpeta destino (y sus ancestros) después de un alta, para que lo recién creado
    /// se vea. Con la reconciliación el resto del árbol ya no se toca, pero si el destino estaba
    /// cerrado el archivo nuevo quedaría adentro, invisible — y el usuario no sabría si se creó.
    /// </summary>
    private void ExpandDirectory(string dir)
    {
        foreach (var root in RootNodes)
        {
            var node = FindNode(root, dir);
            if (node is not { IsDirectory: true }) continue;

            for (var ancestor = node; ancestor is not null; ancestor = ancestor.Parent)
                ancestor.IsExpanded = true;
            return;
        }
    }

    /// <summary>
    /// Resolves the directory a new file/folder should be created in: the selected node's
    /// own directory, or — with nothing selected — the top (first) open vault root
    /// (spec "New File Default Target" / proposal decision #1).
    /// </summary>
    private string? TargetDirectory() =>
        SelectedNode switch
        {
            { IsDirectory: true }  n => n.FullPath,
            { IsDirectory: false } n => Path.GetDirectoryName(n.FullPath),
            _                        => _fileService.VaultRoots.Count > 0 ? _fileService.VaultRoots[0] : null
        };

    /// <summary>
    /// Human-readable label for a create-target directory, shown in the new-file/new-folder
    /// dialog so it's never a surprise which vault a no-selection create lands in: the vault
    /// name alone at root level, or "VaultName/sub/folder" further down.
    /// </summary>
    private string TargetDisplayName(string dir)
    {
        var root = _fileService.GetOwningRoot(dir);
        if (root is null) return Path.GetFileName(dir);

        var vaultName = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
        if (string.Equals(dir, root, StringComparison.OrdinalIgnoreCase))
            return vaultName;

        var relative = Path.GetRelativePath(root, dir).Replace('\\', '/');
        return $"{vaultName}/{relative}";
    }

    private static VaultFileNode ToNode(Models.VaultFile file, VaultFileNode? parent)
    {
        var node = new VaultFileNode
        {
            Name        = file.Name,
            FullPath    = file.FullPath,
            IsDirectory = file.IsDirectory,
            Parent      = parent
        };
        foreach (var child in file.Children)
            node.Children.Add(ToNode(child, node));
        return node;
    }
}

// ─── InputDialog helper ──────────────────────────────────────────────────────

/// <summary>Thin wrapper that opens <see cref="Views.InputDialog"/> and returns the user's input.</summary>
internal static class InputDialog
{
    public static string? Prompt(string title, string label, string defaultValue = "")
    {
        var dlg = new Views.InputDialog(title, label, defaultValue)
        {
            Owner = Application.Current.MainWindow
        };
        return dlg.ShowDialog() == true ? dlg.InputText : null;
    }
}
