using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkdownVault.Models;
using MarkdownVault.Services;
using MarkdownVault.Services.Plugins;

namespace MarkdownVault.ViewModels;

/// <summary>Drives the central editor panel: tabs, file state, auto-save, preview, toolbar actions.</summary>
public partial class EditorViewModel : ObservableObject
{
    private readonly FileService     _fileService;
    private readonly MarkdownService _markdownService;
    private readonly PluginRegistry  _registry;

    // Debounce preview updates so the WebView2 isn't hammered on every keystroke.
    private readonly DispatcherTimer _previewTimer;
    private readonly DispatcherTimer _autoSaveTimer;

    /// <summary>Backing state for the Obsidian-style graph view (nodes, filters, forces).</summary>
    public GraphViewModel Graph { get; }

    public EditorViewModel(FileService fileService, MarkdownService markdownService, PluginRegistry registry)
    {
        _fileService     = fileService;
        _markdownService = markdownService;
        _registry        = registry;

        Graph = new GraphViewModel(new GraphService(fileService));
        Graph.FileOpenRequested += async path => await OpenFileAsync(path);

        // React to files edited outside the app (marshalled to the UI thread).
        _fileService.FileChangedExternally += (_, e) =>
            Application.Current?.Dispatcher.Invoke(() => HandleExternalChange(e.FullPath));

        // Plugin toolbar: build now (empty until plugins load) and rebuild whenever
        // the enabled set changes (activar/desactivar).
        RebuildPluginToolbar();
        _registry.Changed += () =>
            Application.Current?.Dispatcher.Invoke(RebuildPluginToolbar);

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            RefreshPreview();
        };

        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _autoSaveTimer.Tick += async (_, _) => await AutoSaveAsync();
    }

    // ─── Tabs ────────────────────────────────────────────────────────────────

    public ObservableCollection<OpenTab> OpenTabs { get; } = new();

    [ObservableProperty] private OpenTab? _activeTab;

    /// <summary>Raised when the active tab changes so the View can restore scroll/caret.</summary>
    public event Action<OpenTab?>? ActiveTabChanged;

    /// <summary>Raised before switching away from the current tab so the View can save scroll/caret.</summary>
    public event Action<OpenTab?>? ActiveTabSaving;

    // ─── Observable state ────────────────────────────────────────────────────

    [ObservableProperty] private string  _currentFilePath = string.Empty;
    [ObservableProperty] private string  _content         = string.Empty;
    [ObservableProperty] private string  _previewHtml     = string.Empty;
    // Just the rendered markdown fragment (no page shell), for in-place preview updates.
    [ObservableProperty] private string  _previewBodyHtml = string.Empty;
    // Bumped when the preview SHELL changes (plugin set → injected CSS/JS differ). The view
    // uses it to decide when a full reload is required instead of an in-place body patch.
    [ObservableProperty] private int      _previewShellVersion;
    [ObservableProperty] private bool     _isDirty;
    [ObservableProperty] private ViewMode _viewMode        = ViewMode.EditAndPreview;
    [ObservableProperty] private int      _currentLine     = 1;
    [ObservableProperty] private int     _currentColumn   = 1;
    [ObservableProperty] private int     _wordCount;
    [ObservableProperty] private string  _statusMessage   = "Listo";
    [ObservableProperty] private bool    _isDarkTheme;

    /// <summary>When true, the graph view replaces the editor+preview content area.</summary>
    [ObservableProperty] private bool    _showGraph;

    partial void OnShowGraphChanged(bool value)
    {
        if (!value) return;
        SyncGraphActiveFile();
        _ = Graph.BuildAsync();
    }

    [RelayCommand]
    private void ToggleGraph() => ShowGraph = !ShowGraph;

    /// <summary>Pushes the active file's vault-relative path to the graph for highlighting.</summary>
    private void SyncGraphActiveFile()
    {
        var root = _fileService.VaultRoot;
        Graph.ActiveFile = (ActiveTab is not null && !string.IsNullOrEmpty(root))
            ? Path.GetRelativePath(root, ActiveTab.FilePath).Replace('\\', '/')
            : string.Empty;
    }

    // ─── Internal-link navigation ────────────────────────────────────────────

    private readonly Stack<string> _navigationStack = new();

    [ObservableProperty] private bool   _canGoBack;
    [ObservableProperty] private string _goBackFileName = string.Empty;

    public string Title => ActiveTab is null
        ? "MarkdownVault"
        : $"{ActiveTab.FileName}{(IsDirty ? " *" : "")} — MarkdownVault";

    public bool HasFile => !string.IsNullOrEmpty(CurrentFilePath);

    // ─── Content change pipeline ─────────────────────────────────────────────

    partial void OnContentChanged(string value)
    {
        if (ActiveTab is not null && !_isSwitchingTab)
        {
            ActiveTab.Content = value;
            ActiveTab.IsDirty = true;
            IsDirty = true;
        }

        WordCount = CountWords(value);
        OnPropertyChanged(nameof(Title));

        _previewTimer.Stop();
        _previewTimer.Start();
    }

    partial void OnCurrentFilePathChanged(string value)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(HasFile));
    }

    partial void OnIsDirtyChanged(bool value) =>
        OnPropertyChanged(nameof(Title));

    partial void OnViewModeChanged(ViewMode value)
    {
        OnPropertyChanged(nameof(ShowPreviewPanel));
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        RefreshPreview();
    }

    public bool ShowPreviewPanel => ViewMode != ViewMode.EditorOnly;

    // ─── Tab switching ───────────────────────────────────────────────────────

    private bool _isSwitchingTab;

    partial void OnActiveTabChanged(OpenTab? value)
    {
        if (value is null)
        {
            _isSwitchingTab = true;
            CurrentFilePath = string.Empty;
            Content         = string.Empty;
            PreviewHtml     = string.Empty;
            IsDirty         = false;
            _isSwitchingTab = false;
            OnPropertyChanged(nameof(Title));
            SyncGraphActiveFile();
            ActiveTabChanged?.Invoke(null);
            return;
        }

        _isSwitchingTab = true;
        CurrentFilePath = value.FilePath;
        Content         = value.Content;
        IsDirty         = value.IsDirty;
        _isSwitchingTab = false;

        RefreshPreview();
        OnPropertyChanged(nameof(Title));
        SyncGraphActiveFile();
        ActiveTabChanged?.Invoke(value);
    }

    // ─── File operations ─────────────────────────────────────────────────────

    /// <summary>Opens a file into the editor. If already open, activates its tab.</summary>
    public async Task OpenFileAsync(string path)
    {
        // If already open, just switch to it.
        var existing = OpenTabs.FirstOrDefault(t =>
            string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            SwitchToTab(existing);
            return;
        }

        // Save scroll/caret of current tab before switching.
        if (ActiveTab is not null)
        {
            ActiveTabSaving?.Invoke(ActiveTab);
            ActiveTab.IsActive = false;
        }

        // Create new tab.
        var tab = new OpenTab(path)
        {
            Content = await _fileService.ReadFileAsync(path)
        };
        OpenTabs.Add(tab);
        tab.IsActive = true;
        ActiveTab = tab;
    }

    /// <summary>
    /// Navigates to a file via an internal link.  Pushes the current file
    /// onto the navigation stack so the user can go back.
    /// </summary>
    public async Task NavigateToLinkAsync(string resolvedPath)
    {
        if (ActiveTab is not null)
        {
            _navigationStack.Push(ActiveTab.FilePath);
            CanGoBack      = true;
            GoBackFileName = ActiveTab.FileName;
        }
        await OpenFileAsync(resolvedPath);
    }

    [RelayCommand]
    private async Task GoBack()
    {
        if (_navigationStack.Count == 0) return;
        var previousPath = _navigationStack.Pop();
        CanGoBack      = _navigationStack.Count > 0;
        GoBackFileName = _navigationStack.Count > 0
            ? Path.GetFileName(_navigationStack.Peek())
            : string.Empty;
        await OpenFileAsync(previousPath);
    }

    [RelayCommand]
    private void SwitchToTab(OpenTab? tab)
    {
        if (tab is null || tab == ActiveTab) return;

        // Save current tab state.
        if (ActiveTab is not null)
        {
            ActiveTabSaving?.Invoke(ActiveTab);
            ActiveTab.IsActive = false;
        }

        tab.IsActive = true;
        ActiveTab = tab;
    }

    [RelayCommand]
    private async Task CloseTab(OpenTab? tab)
    {
        if (tab is null) return;

        // Dirty check for this specific tab.
        if (tab.IsDirty)
        {
            var result = MessageBox.Show(
                $"'{tab.FileName}' tiene cambios sin guardar. ¿Guardar antes de cerrar?",
                "Cambios sin guardar",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel) return;

            if (result == MessageBoxResult.Yes)
            {
                await _fileService.WriteFileAsync(tab.FilePath, tab.Content);
            }
        }

        var index = OpenTabs.IndexOf(tab);
        OpenTabs.Remove(tab);

        // If we closed the active tab, activate the nearest one.
        if (tab.IsActive)
        {
            if (OpenTabs.Count == 0)
            {
                ActiveTab = null;
            }
            else
            {
                var newIndex = Math.Min(index, OpenTabs.Count - 1);
                SwitchToTab(OpenTabs[newIndex]);
            }
        }
    }

    // ─── External change handling ────────────────────────────────────────────

    /// <summary>
    /// Handles a file that changed on disk outside the app. If the file isn't open,
    /// there's nothing to reconcile (the tree already refreshes). Policy is silent by
    /// design — no modal ever interrupts typing:
    ///   • dirty tab  → keep the user's in-app work (auto-save persists it); ignore the
    ///                  external version. The in-app buffer is the source of truth.
    ///   • clean tab  → reload from disk silently, since there are no unsaved edits to lose.
    /// </summary>
    private async void HandleExternalChange(string fullPath)
    {
        var tab = OpenTabs.FirstOrDefault(t =>
            string.Equals(t.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (tab is null) return;

        if (tab.IsDirty) return;   // in-app changes win, silently — never prompt

        await ReloadTabFromDiskAsync(tab);
    }

    /// <summary>
    /// Reads a file, retrying briefly on <see cref="IOException"/>. An external editor
    /// often holds a short-lived lock while saving, so the watcher can fire before the
    /// file is readable; retrying rides out that window instead of dropping the reload.
    /// Returns <c>null</c> when the file is gone or still locked after all attempts.
    /// </summary>
    private async Task<string?> TryReadWithRetryAsync(string path, int attempts = 5, int delayMs = 80)
    {
        for (int i = 0; i < attempts; i++)
        {
            try { return await _fileService.ReadFileAsync(path); }
            catch (IOException) when (i < attempts - 1) { await Task.Delay(delayMs); }
            catch { return null; }
        }
        return null;
    }

    /// <summary>
    /// Replaces a tab's content with the current file on disk. For the active tab this
    /// refreshes the editor and preview while preserving scroll/caret: it saves the
    /// current position, swaps the content without marking the tab dirty, then restores
    /// the position — reusing the same machinery as a tab switch.
    /// </summary>
    private async Task ReloadTabFromDiskAsync(OpenTab tab)
    {
        var fresh = await TryReadWithRetryAsync(tab.FilePath);
        if (fresh is null) return;  // gone, or still locked after retries — leave buffer

        tab.Content = fresh;
        tab.IsDirty = false;

        if (tab != ActiveTab) return;

        ActiveTabSaving?.Invoke(tab);   // View captures current scroll/caret into the tab

        _isSwitchingTab = true;         // swap content without dirtying the tab
        Content = fresh;
        IsDirty = false;
        _isSwitchingTab = false;

        RefreshPreview();
        ActiveTabChanged?.Invoke(tab);  // View re-applies content and restores scroll/caret
    }

    [RelayCommand]
    private async Task CloseOtherTabs(OpenTab? keepTab)
    {
        if (keepTab is null) return;

        var tabsToClose = OpenTabs.Where(t => t != keepTab).ToList();
        foreach (var tab in tabsToClose)
            await CloseTab(tab);
    }

    [RelayCommand]
    private async Task CloseAllTabs()
    {
        var tabsToClose = OpenTabs.ToList();
        foreach (var tab in tabsToClose)
            await CloseTab(tab);
    }

    [RelayCommand]
    private void NextTab()
    {
        if (OpenTabs.Count < 2 || ActiveTab is null) return;
        var index = OpenTabs.IndexOf(ActiveTab);
        var next  = (index + 1) % OpenTabs.Count;
        SwitchToTab(OpenTabs[next]);
    }

    [RelayCommand]
    private void PreviousTab()
    {
        if (OpenTabs.Count < 2 || ActiveTab is null) return;
        var index = OpenTabs.IndexOf(ActiveTab);
        var prev  = (index - 1 + OpenTabs.Count) % OpenTabs.Count;
        SwitchToTab(OpenTabs[prev]);
    }

    // Always enabled — falls back to Save As when no file is open.
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrEmpty(CurrentFilePath))
        {
            await SaveAsAsync();
            return;
        }
        try
        {
            await _fileService.WriteFileAsync(CurrentFilePath, Content);
            IsDirty = false;
            if (ActiveTab is not null)
                ActiveTab.IsDirty = false;
            StatusMessage = $"Guardado  {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo guardar:\n{ex.Message}",
                "Error al guardar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task SaveAsAsync()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "Guardar como",
            Filter     = "Archivos Markdown|*.md|Archivos HTML|*.html;*.htm|Archivos Mermaid|*.mermaid;*.mmd|Todos los archivos|*.*",
            DefaultExt = ".md",
            FileName   = HasFile ? Path.GetFileName(CurrentFilePath) : "SinTítulo.md"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            await _fileService.WriteFileAsync(dlg.FileName, Content);
            CurrentFilePath = dlg.FileName;
            IsDirty         = false;
            if (ActiveTab is not null)
                ActiveTab.IsDirty = false;
            StatusMessage   = $"Saved  {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo guardar:\n{ex.Message}",
                "Error al guardar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task AutoSaveAsync()
    {
        if (IsDirty && HasFile)
            await SaveAsync();
    }

    // ─── Auto-save control ───────────────────────────────────────────────────

    public void ConfigureAutoSave(bool enabled, int intervalSeconds)
    {
        _autoSaveTimer.Stop();
        if (enabled && intervalSeconds > 0)
        {
            _autoSaveTimer.Interval = TimeSpan.FromSeconds(intervalSeconds);
            _autoSaveTimer.Start();
        }
    }

    // ─── Preview ─────────────────────────────────────────────────────────────

    /// <summary>Re-renders the current file's preview (e.g. after the active plugin set changes).</summary>
    public void RefreshPreviewFromPlugins()
    {
        // Plugin set changed → injected CSS/JS differ → the view must do a full reload,
        // not an in-place body patch. Bump the shell version to signal that.
        PreviewShellVersion++;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (string.IsNullOrEmpty(CurrentFilePath))
        {
            PreviewBodyHtml = string.Empty;
            PreviewHtml     = string.Empty;
            return;
        }

        // NOTE: set PreviewBodyHtml BEFORE PreviewHtml. The view listens on PreviewHtml
        // and reads both, so the body must already be current when that change fires.
        var ext = Path.GetExtension(CurrentFilePath).ToLowerInvariant();
        if (ext == ".html" || ext == ".htm")
        {
            // Raw HTML is a full document, not a fragment → force full navigation.
            PreviewBodyHtml = string.Empty;
            PreviewHtml     = _markdownService.PrepareHtmlForPreview(Content, _fileService.VaultRoot);
        }
        else if (ext == ".mermaid" || ext == ".mmd")
        {
            var markdown = $"```mermaid\n{Content}\n```";
            PreviewBodyHtml = _markdownService.RenderBody(markdown);
            PreviewHtml     = _markdownService.RenderToHtml(markdown, IsDarkTheme, _fileService.VaultRoot);
        }
        else
        {
            PreviewBodyHtml = _markdownService.RenderBody(Content);
            PreviewHtml     = _markdownService.RenderToHtml(Content, IsDarkTheme, _fileService.VaultRoot);
        }
    }

    // ─── Toolbar commands ────────────────────────────────────────────────────

    /// <summary>Raised when the toolbar requests a text insertion/wrapping at the caret.</summary>
    public event Action<string, string>? InsertionRequested;

    /// <summary>Raised to insert a complete snippet (e.g. a Mermaid example) verbatim at the caret.</summary>
    public event Action<string>? SnippetRequested;

    /// <summary>Raised when a plugin command asks to replace the current selection.</summary>
    public event Action<string>? ReplaceSelectionRequested;

    /// <summary>Set by the editor View so plugins can read the current selection.</summary>
    public Func<string>? SelectedTextProvider { get; set; }

    // ─── Plugin toolbar (contributed commands / dropdowns) ───────────────────

    /// <summary>Items de barra aportados por plugins habilitados (botones y menús).</summary>
    public ObservableCollection<PluginToolbarItemViewModel> PluginToolbarItems { get; } = new();

    private EditorContextAdapter? _pluginEditor;

    private void RebuildPluginToolbar()
    {
        PluginToolbarItems.Clear();
        var editor = _pluginEditor ??= new EditorContextAdapter(this);

        foreach (var group in _registry.CommandGroups)
            PluginToolbarItems.Add(PluginToolbarItemViewModel.Group(group, editor));
        foreach (var command in _registry.Commands)
            PluginToolbarItems.Add(PluginToolbarItemViewModel.Single(command, editor));
    }

    // Puentes que usa EditorContextAdapter (traducen a los eventos que maneja la View).
    internal string PluginGetSelectedText()               => SelectedTextProvider?.Invoke() ?? string.Empty;
    internal void   PluginInsertAtCaret(string text)      => SnippetRequested?.Invoke(text);
    internal void   PluginWrapSelection(string b, string a) => InsertionRequested?.Invoke(b, a);
    internal void   PluginReplaceSelection(string text)   => ReplaceSelectionRequested?.Invoke(text);

    [RelayCommand] private void InsertBold()    => InsertionRequested?.Invoke("**", "**");
    [RelayCommand] private void InsertItalic()  => InsertionRequested?.Invoke("*", "*");
    [RelayCommand] private void InsertCode()    => InsertionRequested?.Invoke("`", "`");

    /// <summary>Inserts a fenced code block with the given language tag (e.g. "csharp", "sql").</summary>
    [RelayCommand]
    private void InsertCodeBlock(string language) =>
        InsertionRequested?.Invoke($"```{language}\n", "\n```");

    // NOTE: "Insertar ejemplo Mermaid" se migró al plugin Mermaid (aporta su propio
    // dropdown vía PluginCommandGroup). Al desactivar el plugin, su menú desaparece.

    [RelayCommand] private void InsertHeading1() => InsertionRequested?.Invoke("# ", "");
    [RelayCommand] private void InsertHeading2() => InsertionRequested?.Invoke("## ", "");
    [RelayCommand] private void InsertHeading3() => InsertionRequested?.Invoke("### ", "");
    [RelayCommand] private void InsertBulletList()   => InsertionRequested?.Invoke("- ", "");
    [RelayCommand] private void InsertNumberedList() => InsertionRequested?.Invoke("1. ", "");
    [RelayCommand] private void InsertLink()    => InsertionRequested?.Invoke("[", "](url)");

    [RelayCommand]
    private void InsertInternalLink()
    {
        var files = _fileService.GetAllVaultFiles();
        if (files.Count == 0)
        {
            MessageBox.Show(
                "No hay archivos en el vault. Creá un archivo primero.",
                "Vault vacío", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new Views.LinkPickerDialog(files, CurrentFilePath, _fileService.VaultRoot)
        {
            Owner = Application.Current.MainWindow
        };
        if (dlg.ShowDialog() != true) return;

        InsertionRequested?.Invoke(dlg.ResultMarkdown, "");
    }

    [RelayCommand]
    private void InsertImage()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Seleccionar imagen",
            Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.svg"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var fallback = HasFile ? Path.GetDirectoryName(CurrentFilePath) : null;
            var destPath = _fileService.CopyImageToAssets(dlg.FileName, fallback);
            var md       = _fileService.BuildImageMarkdown(destPath);
            InsertionRequested?.Invoke(md, "");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error de imagen", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void SetViewMode(ViewMode mode) => ViewMode = mode;

    [RelayCommand]
    private void CycleViewMode() => ViewMode = ViewMode switch
    {
        ViewMode.EditorOnly     => ViewMode.EditAndPreview,
        ViewMode.EditAndPreview => ViewMode.ViewerOnly,
        _                       => ViewMode.EditorOnly
    };

    // ─── Drag & drop ─────────────────────────────────────────────────────────

    /// <summary>Handles image files dropped onto the editor.</summary>
    public void HandleDroppedFiles(string[] paths)
    {
        var imageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg" };

        var fallback = HasFile ? Path.GetDirectoryName(CurrentFilePath) : null;
        foreach (var path in paths)
        {
            if (!imageExts.Contains(Path.GetExtension(path))) continue;
            try
            {
                var destPath = _fileService.CopyImageToAssets(path, fallback);
                InsertionRequested?.Invoke(_fileService.BuildImageMarkdown(destPath), "");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error al soltar", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static int CountWords(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
}
