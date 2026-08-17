using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MarkdownVault.ViewModels;

/// <summary>
/// Backs the "Administrar vaults" form: lists every known vault root, lets the user add a
/// new one, and exposes an independent open/close TOGGLE per row (Model A — multi-root:
/// several vaults can be open at once, there is no single "active" row). Every side effect —
/// picking a folder, opening/closing a root, and persistence — is injected as a delegate, so
/// the VM carries no WPF or I/O dependency and is fully unit-testable.
/// </summary>
public partial class VaultsViewModel : ObservableObject
{
    private readonly IList<string>              _knownPaths;
    private readonly Func<IReadOnlyList<string>> _openPaths;
    private readonly Func<string?>               _pickFolder;
    private readonly Action<string>              _openRoot;
    private readonly Action<string>              _closeRoot;
    private readonly Action                      _persist;

    /// <summary>Rows shown in the list, one per known vault.</summary>
    public ObservableCollection<VaultRowViewModel> Vaults { get; } = new();

    public bool HasVaults => Vaults.Count > 0;

    public VaultsViewModel(
        IList<string>                knownPaths,
        Func<IReadOnlyList<string>>  openPaths,
        Func<string?>                pickFolder,
        Action<string>               openRoot,
        Action<string>               closeRoot,
        Action                       persist)
    {
        _knownPaths = knownPaths;
        _openPaths  = openPaths;
        _pickFolder = pickFolder;
        _openRoot   = openRoot;
        _closeRoot  = closeRoot;
        _persist    = persist;
        Build();
    }

    private void Build()
    {
        Vaults.Clear();
        foreach (var path in _knownPaths)
            Vaults.Add(new VaultRowViewModel(path, IsOpen(path)));
        OnPropertyChanged(nameof(HasVaults));
    }

    private bool IsOpen(string path) =>
        _openPaths().Any(p => SamePath(p, path));

    private static bool SamePath(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(a),
            Path.TrimEndingDirectorySeparator(b),
            StringComparison.OrdinalIgnoreCase);

    // ─── Commands ─────────────────────────────────────────────────────────────

    /// <summary>Pick a brand-new root folder, register it, and open it (adds a section,
    /// never closes any other open vault).</summary>
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
        OpenRoot(picked);
    }

    /// <summary>Flips a known vault between open and closed. Opening adds a section without
    /// touching any other open vault or its tabs; closing removes the section and disposes
    /// its watcher but leaves that vault's open tabs editable (decision D3).</summary>
    [RelayCommand]
    private void ToggleOpen(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        if (IsOpen(path)) CloseRoot(path);
        else              OpenRoot(path);
    }

    /// <summary>Remove a vault from the known list. An open vault cannot be removed —
    /// close it first via the toggle.</summary>
    [RelayCommand]
    private void RemoveVault(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || IsOpen(path)) return;

        var match = _knownPaths.FirstOrDefault(p => SamePath(p, path));
        if (match is null) return;

        _knownPaths.Remove(match);
        _persist();
        Build();
    }

    private void OpenRoot(string path)
    {
        _openRoot(path);
        Build();
    }

    private void CloseRoot(string path)
    {
        _closeRoot(path);
        Build();
    }
}

/// <summary>One row in the vaults list: a folder name, its path, and its open/closed state.</summary>
public sealed class VaultRowViewModel
{
    public VaultRowViewModel(string fullPath, bool isOpen)
    {
        FullPath = fullPath;
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath));
        Name     = string.IsNullOrEmpty(name) ? fullPath : name; // drive roots keep the path
        IsOpen   = isOpen;
        Exists   = Directory.Exists(fullPath);
    }

    public string FullPath { get; }
    public string Name     { get; }

    /// <summary>Whether this vault is currently open (one of possibly several — no single
    /// "active" row anymore).</summary>
    public bool IsOpen { get; }

    /// <summary>Whether the folder still exists on disk.</summary>
    public bool Exists { get; }

    /// <summary>True when the folder was moved or deleted since it was added.</summary>
    public bool IsMissing => !Exists;

    /// <summary>An open vault cannot be removed from the known list — close it first.</summary>
    public bool IsRemovable => !IsOpen;

    /// <summary>The open/close toggle stays usable even for a missing folder as long as it's
    /// already open (so the user can still close it); opening a missing folder is blocked.</summary>
    public bool CanToggle => IsOpen || Exists;
}
