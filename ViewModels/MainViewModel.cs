using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkdownVault.Models;
using MarkdownVault.Services;

namespace MarkdownVault.ViewModels;

/// <summary>Root ViewModel that orchestrates the file tree, editor, and global settings.</summary>
public partial class MainViewModel : ObservableObject
{
    private readonly FileService     _fileService;
    private readonly SettingsService _settingsService;
    private          AppSettings     _settings;

    public FileTreeViewModel FileTree { get; }
    public EditorViewModel   Editor   { get; }

    public MainViewModel(
        FileService     fileService,
        MarkdownService markdownService,
        SettingsService settingsService)
    {
        _fileService     = fileService;
        _settingsService = settingsService;
        _settings        = settingsService.Load();

        FileTree = new FileTreeViewModel(fileService);
        Editor   = new EditorViewModel(fileService, markdownService);

        // Wire file-open requests from the tree into the editor.
        FileTree.FileOpenRequested += async path => await Editor.OpenFileAsync(path);

        // Apply persisted settings.
        Editor.ViewMode = _settings.ViewMode;
        Editor.ConfigureAutoSave(_settings.AutoSaveEnabled, _settings.AutoSaveIntervalSec);
        IsDarkTheme = _settings.IsDarkTheme;
        Editor.IsDarkTheme = IsDarkTheme;
        ApplyTheme(IsDarkTheme);
        IsExplorerVisible = _settings.IsExplorerVisible;
        FontFamily  = _settings.FontFamily;
        FontSize    = _settings.FontSize;
        PreviewZoom = _settings.PreviewZoom;

        // Restore last vault if it still exists.
        if (!string.IsNullOrWhiteSpace(_settings.LastVaultPath) &&
            System.IO.Directory.Exists(_settings.LastVaultPath))
        {
            OpenVaultPath(_settings.LastVaultPath);
        }
    }

    // ─── Observable properties ───────────────────────────────────────────────

    [ObservableProperty] private bool   _isDarkTheme;
    [ObservableProperty] private bool   _isExplorerVisible = true;

    partial void OnIsDarkThemeChanged(bool value)
    {
        Editor.IsDarkTheme = value;
    }
    [ObservableProperty] private string _fontFamily  = "Consolas";
    [ObservableProperty] private double _fontSize    = 14;
    [ObservableProperty] private double _previewZoom = 1.0;

    public string VaultName =>
        string.IsNullOrEmpty(_fileService.VaultRoot)
            ? "Sin vault abierto"
            : System.IO.Path.GetFileName(_fileService.VaultRoot);

    // ─── Commands ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenVault()
    {
        // OpenFolderDialog is built into WPF on .NET 8+ — no Windows Forms needed.
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Seleccionar carpeta del vault"
        };
        if (dlg.ShowDialog() != true) return;
        OpenVaultPath(dlg.FolderName);
    }

    [RelayCommand]
    private void NewFile() => FileTree.CreateFileCommand.Execute(null);

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        ApplyTheme(IsDarkTheme);
        SaveSettings();
    }

    [RelayCommand]
    private void ToggleExplorer()
    {
        IsExplorerVisible = !IsExplorerVisible;
        SaveSettings();
    }

    [RelayCommand]
    private void IncreaseFontSize()
    {
        FontSize = Math.Min(FontSize + 1, 36);
        PreviewZoom = Math.Min(PreviewZoom + 0.1, 5.0);
        SaveSettings();
    }

    [RelayCommand]
    private void DecreaseFontSize()
    {
        FontSize = Math.Max(FontSize - 1, 8);
        PreviewZoom = Math.Max(PreviewZoom - 0.1, 0.25);
        SaveSettings();
    }

    [RelayCommand]
    private void ResetPreviewZoom()
    {
        PreviewZoom = 1.0;
        FontSize = 14;
        SaveSettings();
    }

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    /// <summary>Call on application exit to persist current settings.</summary>
    public void OnExit()
    {
        SaveSettings();
        _fileService.Dispose();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private void OpenVaultPath(string path)
    {
        _fileService.OpenVault(path);
        FileTree.LoadVault(path);
        _settings.LastVaultPath = path;
        OnPropertyChanged(nameof(VaultName));
        SaveSettings();
    }

    private void ApplyTheme(bool dark)
    {
        var dicts = Application.Current.Resources.MergedDictionaries;
        dicts.Clear();

        var theme = dark ? "DarkTheme" : "LightTheme";
        dicts.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Resources/Themes/{theme}.xaml")
        });
    }

    private void SaveSettings()
    {
        _settings.IsDarkTheme         = IsDarkTheme;
        _settings.IsExplorerVisible   = IsExplorerVisible;
        _settings.FontFamily          = FontFamily;
        _settings.FontSize            = FontSize;
        _settings.PreviewZoom         = PreviewZoom;
        _settings.ViewMode            = Editor.ViewMode;
        _settings.AutoSaveEnabled     = true;
        _settings.AutoSaveIntervalSec = 30;
        _settingsService.Save(_settings);
    }
}
