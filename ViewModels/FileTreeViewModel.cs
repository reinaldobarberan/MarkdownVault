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
    /// Rebuilds one root's section from disk in place — used both by the scoped
    /// <see cref="FileService.VaultChanged"/> handler and by the CRUD commands below, so a
    /// local create/rename/delete refreshes only the affected vault, never every open one.
    /// A stale root (already closed via <see cref="RemoveRoot"/>, e.g. a late watcher
    /// callback) is silently ignored.
    /// </summary>
    private void RefreshRoot(string root)
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

        var wasExpanded = RootNodes[index].IsExpanded;
        var tree = _fileService.BuildTree(root);
        var node = ToNode(tree, null);
        node.IsExpanded = wasExpanded;
        RootNodes[index] = node;
    }

    /// <summary>Refreshes whichever open root owns <paramref name="path"/>, if any.</summary>
    private void RefreshOwnerOf(string path)
    {
        if (_fileService.GetOwningRoot(path) is { } root)
            RefreshRoot(root);
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
