using System.IO;
using MarkdownVault.Models;
using MarkdownVault.Services;
using MarkdownVault.Services.Plugins;
using MarkdownVault.ViewModels;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Covers the one-time <c>LastVaultPath</c> → <c>OpenVaultPaths</c> settings migration
/// (Phase 2, multi-vault change; spec "One-Time Settings Migration"): seeds the new
/// multi-root open set from the legacy single-path setting exactly once, guarded by
/// <see cref="AppSettings.VaultPathsMigrated"/>, and never resurrects a vault the user has
/// since closed. Also covers restoring every entry in <c>OpenVaultPaths</c> on startup.
///
/// Exercised through the real <see cref="MainViewModel"/> ctor (where
/// <c>MigrateVaultPathsIfNeeded</c> runs) against an isolated settings.json — same
/// convention as <see cref="WorkbenchInvariantTests"/>/<see cref="CompareFilesCommandTests"/>
/// (real FileService/SettingsService against a temp dir, no mocking framework).
/// </summary>
public class VaultMigrationTests : IDisposable
{
    private readonly string         _root;
    private readonly string         _settingsPath;
    private readonly FileService    _fileService = new();
    private readonly PluginRegistry _registry    = new();
    private readonly MarkdownService _markdownService;
    private readonly FakeDialogService _dialogService = new();

    public VaultMigrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"mvmig_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _settingsPath    = Path.Combine(_root, "settings.json");
        _markdownService = new MarkdownService(_registry);
    }

    private MainViewModel CreateVm() =>
        new(_fileService, _markdownService,
            new SettingsService(_settingsPath),
            _registry, _dialogService, uiDispatch: a => a());

    private string NewVaultDir(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void First_launch_seeds_OpenVaultPaths_from_LastVaultPath_once()
    {
        var vaultPath = NewVaultDir("Vault");
        new SettingsService(_settingsPath).Save(new AppSettings
        {
            LastVaultPath      = vaultPath,
            VaultPathsMigrated = false
        });

        _ = CreateVm();

        var reloaded = new SettingsService(_settingsPath).Load();
        Assert.True(reloaded.VaultPathsMigrated);
        Assert.Equal(new[] { vaultPath }, reloaded.OpenVaultPaths);
    }

    [Fact]
    public void Migration_is_a_noop_when_the_flag_is_already_set()
    {
        var vaultPath = NewVaultDir("Vault");
        var otherPath = NewVaultDir("NeverOpened");
        new SettingsService(_settingsPath).Save(new AppSettings
        {
            LastVaultPath      = otherPath, // must be ignored — migration already ran
            OpenVaultPaths     = new List<string> { vaultPath },
            VaultPathsMigrated = true
        });

        _ = CreateVm();

        var reloaded = new SettingsService(_settingsPath).Load();
        Assert.Equal(new[] { vaultPath }, reloaded.OpenVaultPaths);
    }

    [Fact]
    public void Deliberately_closed_vault_is_not_resurrected_on_relaunch()
    {
        // Migration already ran once (flag set) and the user has since closed the vault
        // it seeded — OpenVaultPaths is empty. A relaunch must NOT re-seed it from
        // LastVaultPath (spec: "Deliberately closed vault stays closed").
        var vaultPath = NewVaultDir("Vault");
        new SettingsService(_settingsPath).Save(new AppSettings
        {
            LastVaultPath      = vaultPath,
            OpenVaultPaths     = new List<string>(),
            VaultPathsMigrated = true
        });

        _ = CreateVm();

        var reloaded = new SettingsService(_settingsPath).Load();
        Assert.Empty(reloaded.OpenVaultPaths);
    }

    [Fact]
    public void Restore_opens_every_entry_in_OpenVaultPaths_on_startup()
    {
        var vaultA = NewVaultDir("VaultA");
        var vaultB = NewVaultDir("VaultB");
        new SettingsService(_settingsPath).Save(new AppSettings
        {
            OpenVaultPaths     = new List<string> { vaultA, vaultB },
            VaultPathsMigrated = true
        });

        _ = CreateVm();

        Assert.Contains(_fileService.VaultRoots,
            r => string.Equals(r, Path.GetFullPath(vaultA), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(_fileService.VaultRoots,
            r => string.Equals(r, Path.GetFullPath(vaultB), StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        _fileService.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
