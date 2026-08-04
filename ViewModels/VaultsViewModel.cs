using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MarkdownVault.ViewModels;

/// <summary>
/// Backs the "Administrar vaults" form: lists the known vault roots, lets the user
/// add a new root folder (which becomes the active vault), switch between existing
/// ones, and remove entries. Every side effect — picking a folder, actually opening
/// a vault, and persistence — is injected as a delegate, so the VM carries no WPF or
/// I/O dependency and is fully unit-testable.
/// </summary>
public partial class VaultsViewModel : ObservableObject
{
    private readonly IList<string> _knownPaths;
    private readonly Func<string?> _pickFolder;
    private readonly Action<string> _openVault;
    private readonly Action _persist;

    /// <summary>Rows shown in the list, one per known vault.</summary>
    public ObservableCollection<VaultRowViewModel> Vaults { get; } = new();

    /// <summary>Full path of the currently open vault, or <c>null</c> when none is open.</summary>
    [ObservableProperty] private string? _activePath;

    public bool HasVaults => Vaults.Count > 0;

    public VaultsViewModel(
        IList<string>   knownPaths,
        string?         activePath,
        Func<string?>   pickFolder,
        Action<string>  openVault,
        Action          persist)
    {
        _knownPaths = knownPaths;
        _pickFolder = pickFolder;
        _openVault  = openVault;
        _persist    = persist;
        _activePath = activePath;
        Build();
    }

    private void Build()
    {
        Vaults.Clear();
        foreach (var path in _knownPaths)
            Vaults.Add(new VaultRowViewModel(path, IsActive(path)));
        OnPropertyChanged(nameof(HasVaults));
    }

    private bool IsActive(string path) =>
        ActivePath is not null && SamePath(path, ActivePath);

    private static bool SamePath(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(a),
            Path.TrimEndingDirectorySeparator(b),
            StringComparison.OrdinalIgnoreCase);

    // ─── Commands ─────────────────────────────────────────────────────────────

    /// <summary>Pick a brand-new root folder, register it, and switch to it.</summary>
    [RelayCommand]
    private void AddVault()
    {
        var picked = _pickFolder();
        if (string.IsNullOrWhiteSpace(picked)) return;

        if (!_knownPaths.Any(p => SamePath(p, picked)))
        {
            _knownPaths.Add(picked);
            _persist();
        }
        Activate(picked);
    }

    /// <summary>Switch the app to an already-known vault.</summary>
    [RelayCommand]
    private void OpenVault(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Activate(path);
    }

    /// <summary>Remove a vault from the list. The active vault cannot be removed.</summary>
    [RelayCommand]
    private void RemoveVault(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || IsActive(path)) return;

        var match = _knownPaths.FirstOrDefault(p => SamePath(p, path));
        if (match is null) return;

        _knownPaths.Remove(match);
        _persist();
        Build();
    }

    private void Activate(string path)
    {
        _openVault(path);
        ActivePath = path;
        Build();
    }
}

/// <summary>One row in the vaults list: a folder name, its path, and its state.</summary>
public sealed class VaultRowViewModel
{
    public VaultRowViewModel(string fullPath, bool isActive)
    {
        FullPath = fullPath;
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath));
        Name     = string.IsNullOrEmpty(name) ? fullPath : name; // drive roots keep the path
        IsActive = isActive;
        Exists   = Directory.Exists(fullPath);
    }

    public string FullPath { get; }
    public string Name     { get; }
    public bool   IsActive { get; }

    /// <summary>Whether the folder still exists on disk.</summary>
    public bool Exists { get; }

    /// <summary>True when the folder was moved or deleted since it was added.</summary>
    public bool IsMissing => !Exists;

    /// <summary>The active vault cannot be removed from the list.</summary>
    public bool IsRemovable => !IsActive;
}
