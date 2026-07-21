using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkdownVault.Models;
using MarkdownVault.Services;

namespace MarkdownVault.ViewModels;

/// <summary>Drives the central editor panel: tabs, file state, auto-save, preview, toolbar actions.</summary>
public partial class EditorViewModel : ObservableObject
{
    private readonly FileService     _fileService;
    private readonly MarkdownService _markdownService;

    // Debounce preview updates so the WebView2 isn't hammered on every keystroke.
    private readonly DispatcherTimer _previewTimer;
    private readonly DispatcherTimer _autoSaveTimer;

    /// <summary>Backing state for the Obsidian-style graph view (nodes, filters, forces).</summary>
    public GraphViewModel Graph { get; }

    public EditorViewModel(FileService fileService, MarkdownService markdownService)
    {
        _fileService     = fileService;
        _markdownService = markdownService;

        Graph = new GraphViewModel(new GraphService(fileService));
        Graph.FileOpenRequested += async path => await OpenFileAsync(path);

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

    private void RefreshPreview()
    {
        if (string.IsNullOrEmpty(CurrentFilePath))
        {
            PreviewHtml = string.Empty;
            return;
        }

        var ext = Path.GetExtension(CurrentFilePath).ToLowerInvariant();
        if (ext == ".html" || ext == ".htm")
        {
            PreviewHtml = _markdownService.PrepareHtmlForPreview(Content, _fileService.VaultRoot);
        }
        else if (ext == ".mermaid" || ext == ".mmd")
        {
            var markdown = $"```mermaid\n{Content}\n```";
            PreviewHtml = _markdownService.RenderToHtml(markdown, IsDarkTheme, _fileService.VaultRoot);
        }
        else
        {
            PreviewHtml = _markdownService.RenderToHtml(Content, IsDarkTheme, _fileService.VaultRoot);
        }
    }

    // ─── Toolbar commands ────────────────────────────────────────────────────

    /// <summary>Raised when the toolbar requests a text insertion/wrapping at the caret.</summary>
    public event Action<string, string>? InsertionRequested;

    /// <summary>Raised to insert a complete snippet (e.g. a Mermaid example) verbatim at the caret.</summary>
    public event Action<string>? SnippetRequested;

    [RelayCommand] private void InsertBold()    => InsertionRequested?.Invoke("**", "**");
    [RelayCommand] private void InsertItalic()  => InsertionRequested?.Invoke("*", "*");
    [RelayCommand] private void InsertCode()    => InsertionRequested?.Invoke("`", "`");

    /// <summary>Inserts a fenced code block with the given language tag (e.g. "csharp", "sql").</summary>
    [RelayCommand]
    private void InsertCodeBlock(string language) =>
        InsertionRequested?.Invoke($"```{language}\n", "\n```");

    /// <summary>Inserts a ready-to-render Mermaid example diagram at the caret.</summary>
    [RelayCommand]
    private void InsertMermaidExample(string kind)
    {
        var body = kind switch
        {
            "flowchart" => """
                flowchart TD
                    A([Inicio]) --> B{"¿Condición?"}
                    B -->|Sí| C["Procesar datos"]
                    B -->|No| D["Terminar"]
                    C --> D
                """,
            "sequence" => """
                sequenceDiagram
                    participant U as Usuario
                    participant A as App
                    participant S as Servidor
                    U->>A: Abrir archivo
                    A->>S: Pedir datos
                    S-->>A: Devolver datos
                    A-->>U: Mostrar contenido
                """,
            "class" => """
                classDiagram
                    class Nota {
                        +String titulo
                        +String contenido
                        +guardar()
                    }
                    class Vault {
                        +List~Nota~ notas
                        +abrir()
                    }
                    Vault "1" o-- "muchas" Nota
                """,
            "state" => """
                stateDiagram-v2
                    [*] --> Borrador
                    Borrador --> Revision : enviar
                    Revision --> Publicado : aprobar
                    Revision --> Borrador : rechazar
                    Publicado --> [*]
                """,
            "gantt" => """
                gantt
                    title Cronograma del proyecto
                    dateFormat YYYY-MM-DD
                    section Planificación
                    Análisis       :done,   a1, 2024-01-01, 5d
                    Diseño         :active, a2, after a1, 4d
                    section Desarrollo
                    Implementación :        a3, after a2, 10d
                """,
            "pie" => """
                pie title Distribución de leads por ramo
                    "Auto" : 40
                    "Salud" : 30
                    "Vida" : 20
                    "Viaje" : 10
                """,
            "mindmap" => """
                mindmap
                  root((MarkdownVault))
                    Editor
                      Formato
                      Atajos
                    Vista previa
                      Mermaid
                      Tablas
                    Grafo
                """,
            "timeline" => """
                timeline
                    title Evolución del proyecto
                    2023 : Idea inicial
                    2024 : Primer prototipo : Vista de grafo
                    2025 : Lanzamiento
                """,
            _ => """
                flowchart LR
                    A --> B --> C
                """
        };

        SnippetRequested?.Invoke($"```mermaid\n{body}\n```\n");
    }
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
