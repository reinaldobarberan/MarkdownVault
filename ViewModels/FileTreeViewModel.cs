using System.Collections.ObjectModel;
using System.IO;
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
        _fileService.VaultChanged += (_, _) =>
            Application.Current.Dispatcher.Invoke(Refresh);
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

    /// <summary>Builds the tree from the vault on disk.</summary>
    public void LoadVault(string path)
    {
        var tree = _fileService.BuildTree(path);
        RootNodes = new ObservableCollection<VaultFileNode> { ToNode(tree, null) };
        if (RootNodes.Count > 0)
            RootNodes[0].IsExpanded = true;
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

        var name = InputDialog.Prompt("Nuevo archivo", "Nombre del archivo:", "NuevoArchivo.md");
        if (string.IsNullOrWhiteSpace(name)) return;

        var path = _fileService.CreateFile(dir, name);
        Refresh();
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

        var name = InputDialog.Prompt("Nueva carpeta", "Nombre de la carpeta:", "Nueva carpeta");
        if (string.IsNullOrWhiteSpace(name)) return;

        _fileService.CreateDirectory(dir, name);
        Refresh();
    }

    [RelayCommand]
    private void RenameNode(VaultFileNode? node)
    {
        if (node is null) return;

        var newName = InputDialog.Prompt("Renombrar", "Nuevo nombre:", node.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == node.Name) return;

        _fileService.Rename(node.FullPath, newName);
        Refresh();
    }

    [RelayCommand]
    private void DeleteNode(VaultFileNode? node)
    {
        if (node is null) return;

        var msg    = node.IsDirectory ? $"¿Eliminar la carpeta '{node.Name}' y todo su contenido?" : $"¿Eliminar '{node.Name}'?";
        var result = MessageBox.Show(msg, "Confirmar eliminación",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _fileService.Delete(node.FullPath);
        Refresh();
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

    private void Refresh()
    {
        if (_fileService.VaultRoot is not null)
            LoadVault(_fileService.VaultRoot);
    }

    private string? TargetDirectory() =>
        SelectedNode switch
        {
            { IsDirectory: true }  n => n.FullPath,
            { IsDirectory: false } n => Path.GetDirectoryName(n.FullPath),
            _                        => _fileService.VaultRoot
        };

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
