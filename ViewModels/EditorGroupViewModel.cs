using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkdownVault.Models;
using MarkdownVault.PluginSdk;
using MarkdownVault.Services;
using MarkdownVault.Services.Plugins;

namespace MarkdownVault.ViewModels;

/// <summary>Drives the central editor panel: tabs, file state, auto-save, preview, toolbar actions.</summary>
public partial class EditorGroupViewModel : ObservableObject
{
    private readonly FileService     _fileService;
    private readonly MarkdownService _markdownService;
    private readonly PluginRegistry  _registry;
    private readonly IDialogService  _dialogService;

    // Marshals callbacks that may originate off the UI thread (e.g. FileSystemWatcher
    // events) onto the dispatcher. Injected so tests can run them synchronously
    // (`a => a()`) — Application.Current is null under xUnit, so the production default
    // would otherwise silently swallow the callback and leave this path uncovered.
    private readonly Action<Action> _uiDispatch;

    // Debounce preview updates so the WebView2 isn't hammered on every keystroke.
    private readonly DispatcherTimer _previewTimer;
    private readonly DispatcherTimer _autoSaveTimer;

    /// <summary>
    /// Workbench-owned status sink (Phase 2: StatusMessage promoted to MainViewModel).
    /// Null in tests — writes are simply dropped, matching RedirectIfOwnedElsewhere's
    /// "null in tests → group behaves standalone" convention.
    /// </summary>
    internal Action<string>? StatusSink { get; set; }

    /// <summary>
    /// Workbench-owned path-uniqueness redirect (Phase 3). Checked before this group's own
    /// intra-group tab dedup in <see cref="OpenFileAsync"/>. Returns true when another group
    /// already owns the path and has been focused/switched instead of opening a duplicate.
    /// Null in tests → group behaves standalone (single-group mode).
    /// </summary>
    internal Func<string, bool>? RedirectIfOwnedElsewhere { get; set; }

    /// <summary>
    /// Raised when the View reports this group received focus (mouse press anywhere in the
    /// pane, or keyboard focus arriving). The workbench subscribes at group-creation time and
    /// calls its own <c>SetFocus</c> — the group never reaches up to the workbench directly.
    /// </summary>
    internal event Action<EditorGroupViewModel>? FocusRequested;

    /// <summary>
    /// Called by <c>EditorView.xaml.cs</c> on <c>PreviewMouseDown</c> (tunnelling — catches
    /// tab-strip/toolbar clicks that never move keyboard focus, since Border/TextBlock/
    /// ItemsControl are Focusable=false) and on <c>GotKeyboardFocus</c> (keyboard/programmatic
    /// focus). Neither alone is sufficient; both call this. Idempotent by construction — the
    /// workbench's SetFocus no-ops when this group is already focused.
    /// </summary>
    internal void NotifyFocused() => FocusRequested?.Invoke(this);

    public EditorGroupViewModel(
        FileService fileService,
        MarkdownService markdownService,
        PluginRegistry registry,
        IDialogService dialogService,
        Action<Action>? uiDispatch = null)
    {
        _fileService     = fileService;
        _markdownService = markdownService;
        _registry        = registry;
        _dialogService   = dialogService;
        _uiDispatch      = uiDispatch ??
            (a => { if (Application.Current is { } app) app.Dispatcher.Invoke(a); else a(); });

        // Plugin toolbar: build now (empty until plugins load) and rebuild whenever
        // the enabled set changes (activar/desactivar).
        RebuildPluginToolbar();
        _registry.Changed += () => _uiDispatch(RebuildPluginToolbar);

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

    /// <summary>Raised when a file is opened via an internal link (only from <see cref="NavigateToLinkAsync"/>),
    /// so the workbench can reveal that file in the file-tree. Deliberately NOT fired on plain tab
    /// switches — internal-link navigation is the sole trigger for auto-reveal.</summary>
    public event Action<string>? LinkNavigated;

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
    [ObservableProperty] private int      _currentLine     = 1;
    [ObservableProperty] private int     _currentColumn   = 1;
    [ObservableProperty] private int     _wordCount;
    [ObservableProperty] private bool    _isDarkTheme;

    // ─── Internal-link navigation ────────────────────────────────────────────

    private readonly Stack<string> _navigationStack = new();

    [ObservableProperty] private bool   _canGoBack;
    [ObservableProperty] private string _goBackFileName = string.Empty;

    public string Title => ActiveTab is null
        ? "MarkdownVault"
        : $"{ActiveTab.FileName}{(IsDirty ? " *" : "")} — MarkdownVault";

    public bool HasFile => !string.IsNullOrEmpty(CurrentFilePath);

    /// <summary>
    /// True cuando este panel tiene una pestaña abierta. Es la ÚNICA señal válida de «hay
    /// documento sobre el que editar»: <see cref="HasFile"/> mira <see cref="CurrentFilePath"/>,
    /// que <see cref="SaveAsAsync"/> deja seteado tras un Guardar como, y eso NO implica que
    /// exista una <see cref="OpenTab"/> detrás donde persistir lo que se escriba.
    /// </summary>
    public bool HasOpenDocument => ActiveTab is not null;

    /// <summary>
    /// Sin documento no se edita. La vista ata el área de texto a esta propiedad porque escribir
    /// en el vacío no guardaba en ningún lado: <see cref="OnContentChanged"/> solo persiste en
    /// <see cref="ActiveTab"/>, así que el texto vivía únicamente en el control y la primera
    /// apertura de archivo lo pisaba sin aviso.
    /// </summary>
    public bool IsEditorReadOnly => !HasOpenDocument;

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

    partial void OnIsDarkThemeChanged(bool value)
    {
        RefreshPreview();
    }

    // ─── Tab switching ───────────────────────────────────────────────────────

    private bool _isSwitchingTab;

    partial void OnActiveTabChanged(OpenTab? value)
    {
        // Antes que nada: abrir o cerrar la última pestaña cambia si el panel es editable.
        NotifyDocumentGates();

        if (value is null)
        {
            _isSwitchingTab = true;
            CurrentFilePath = string.Empty;
            Content         = string.Empty;
            PreviewHtml     = string.Empty;
            IsDirty         = false;
            _isSwitchingTab = false;
            OnPropertyChanged(nameof(Title));
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
        ActiveTabChanged?.Invoke(value);
    }

    /// <summary>
    /// Reevalúa todo lo que depende de «hay documento abierto»: las dos propiedades que mira la
    /// vista y el <c>CanExecute</c> de cada comando que escribe en el editor.
    ///
    /// Los comandos hay que avisarlos UNO POR UNO a propósito: <c>RelayCommand</c> no observa
    /// nada, solo vuelve a consultar su <c>CanExecute</c> cuando alguien le levanta la mano. Un
    /// comando que falte acá queda con el botón habilitado sobre un panel vacío, que es
    /// exactamente el agujero que este cambio cierra.
    /// </summary>
    private void NotifyDocumentGates()
    {
        OnPropertyChanged(nameof(HasOpenDocument));
        OnPropertyChanged(nameof(IsEditorReadOnly));

        SaveCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();

        InsertBoldCommand.NotifyCanExecuteChanged();
        InsertItalicCommand.NotifyCanExecuteChanged();
        InsertCodeCommand.NotifyCanExecuteChanged();
        InsertCodeBlockCommand.NotifyCanExecuteChanged();
        InsertHeading1Command.NotifyCanExecuteChanged();
        InsertHeading2Command.NotifyCanExecuteChanged();
        InsertHeading3Command.NotifyCanExecuteChanged();
        InsertBulletListCommand.NotifyCanExecuteChanged();
        InsertNumberedListCommand.NotifyCanExecuteChanged();
        InsertLinkCommand.NotifyCanExecuteChanged();
        InsertInternalLinkCommand.NotifyCanExecuteChanged();
        InsertImageCommand.NotifyCanExecuteChanged();

        foreach (var item in PluginToolbarItems)
            item.NotifyCanExecuteChanged();
    }

    // ─── File operations ─────────────────────────────────────────────────────

    /// <summary>
    /// Opens a file into the editor. If already open, activates its tab. Checks
    /// <see cref="RedirectIfOwnedElsewhere"/> FIRST (Phase 3 path-uniqueness invariant) —
    /// if another group already owns the path, the workbench focuses that group instead
    /// of this one opening a duplicate.
    /// </summary>
    public async Task OpenFileAsync(string path)
    {
        if (RedirectIfOwnedElsewhere?.Invoke(path) == true) return;

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
        LinkNavigated?.Invoke(resolvedPath);
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
            var result = _dialogService.ConfirmSaveChanges(tab.FileName);

            if (result == ConfirmResult.Cancel) return;

            if (result == ConfirmResult.Yes)
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

    // ─── Path-uniqueness invariant (Phase 3) ─────────────────────────────────

    /// <summary>Whether this group has a tab open for <paramref name="path"/>.</summary>
    internal bool Owns(string path) =>
        OpenTabs.Any(t => string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns this group's tab for <paramref name="path"/>, or null.</summary>
    internal OpenTab? Find(string path) =>
        OpenTabs.FirstOrDefault(t => string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));

    // ─── External change handling ────────────────────────────────────────────

    /// <summary>
    /// Reconciles a file that changed on disk outside the app, called by the workbench
    /// (<see cref="MainViewModel"/>) after it has already determined THIS group owns the
    /// path. Policy is silent by design — no modal ever interrupts typing:
    ///   • dirty tab  → keep the user's in-app work (auto-save persists it); ignore the
    ///                  external version. The in-app buffer is the source of truth.
    ///   • clean tab  → reload from disk silently, since there are no unsaved edits to lose.
    /// </summary>
    internal async Task ReloadIfCleanAsync(string fullPath)
    {
        var tab = Find(fullPath);
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
    internal async Task ReloadTabFromDiskAsync(OpenTab tab)
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

    // Requiere pestaña abierta. Antes estaba SIEMPRE habilitado y sin archivo caía en
    // SaveAsAsync, que escribía el contenido huérfano del control y seteaba CurrentFilePath
    // sin crear la pestaña: quedaba HasFile == true con ActiveTab == null, o sea la barra de
    // pestañas vacía y el ViewModel convencido de tener un archivo abierto.
    [RelayCommand(CanExecute = nameof(HasOpenDocument))]
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
            StatusSink?.Invoke($"Guardado  {DateTime.Now:HH:mm:ss}");
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"No se pudo guardar:\n{ex.Message}", "Error al guardar");
        }
    }

    [RelayCommand(CanExecute = nameof(HasOpenDocument))]
    private async Task SaveAsAsync()
    {
        var suggestedFileName = HasFile ? Path.GetFileName(CurrentFilePath) : "SinTítulo.md";
        const string filter = "Archivos Markdown|*.md|Archivos HTML|*.html;*.htm|Archivos Mermaid|*.mermaid;*.mmd|Todos los archivos|*.*";
        var filePath = _dialogService.AskSaveFilePath(suggestedFileName, filter, ".md");
        if (filePath is null) return;

        try
        {
            await _fileService.WriteFileAsync(filePath, Content);
            CurrentFilePath = filePath;
            IsDirty         = false;
            if (ActiveTab is not null)
            {
                ActiveTab.IsDirty  = false;
                // Bug #273 fix: re-key the tab's identity so the path-uniqueness
                // invariant's Owns()/Find() lookups stay correct after Save-As.
                ActiveTab.FilePath = filePath;
            }
            StatusSink?.Invoke($"Saved  {DateTime.Now:HH:mm:ss}");
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"No se pudo guardar:\n{ex.Message}", "Error al guardar");
        }
    }

    /// <summary>
    /// Guardado automático: persiste TODAS las pestañas sucias de este panel, no solo la activa.
    ///
    /// Antes era <c>if (IsDirty &amp;&amp; HasFile) await SaveAsync()</c>, y <see cref="SaveAsync"/>
    /// escribe <see cref="CurrentFilePath"/> con <see cref="Content"/>: el documento de adelante y
    /// nada más. Eso alcanzaba mientras todo el texto entraba por el teclado, que siempre va a la
    /// pestaña activa. Desde que las escrituras de plugin se fijan a la pestaña donde ARRANCÓ la
    /// acción (<see cref="PinnedEditorContext"/>), el dictado puede estar llenando una pestaña de
    /// segundo plano —una frase por pausa, durante minutos— y ese texto quedaba marcado como
    /// modificado pero solo en memoria: ni un byte tocaba el disco.
    /// </summary>
    private async Task AutoSaveAsync()
    {
        await SaveDirtyTabsAsync();
    }

    /// <summary>
    /// Escribe a disco todas las pestañas sucias del panel y devuelve cuántas guardó.
    /// La fuente del texto de cada una la decide <see cref="DirtyTabScanner"/>: activa ⇒ contenido
    /// VIVO del panel, en segundo plano ⇒ su propio <see cref="OpenTab.Content"/>. Confundirlas no
    /// sería "no guardar" sino guardar texto viejo encima del bueno.
    /// </summary>
    internal async Task<int> SaveDirtyTabsAsync()
    {
        var pending = DirtyTabScanner.Scan(OpenTabs, ActiveTab, Content);
        if (pending.Count == 0) return 0;

        var saved  = 0;
        var failed = new List<string>();

        foreach (var item in pending)
        {
            try
            {
                await _fileService.WriteFileAsync(item.FilePath, item.Content);
                MarkSaved(item);
                saved++;
            }
            catch (Exception ex)
            {
                failed.Add($"{item.Tab.FileName}: {ex.Message}");
            }
        }

        ReportSaveOutcome(saved, failed);
        return saved;
    }

    /// <summary>
    /// Variante SÍNCRONA de <see cref="SaveDirtyTabsAsync"/> para el cierre de la ventana.
    /// Devuelve los fallos (vacío = todo persistido). Ver <see cref="FileService.WriteFile"/> para
    /// por qué acá no se puede bloquear sobre la versión async.
    /// </summary>
    internal IReadOnlyList<string> SaveDirtyTabsBlocking()
    {
        var pending = DirtyTabScanner.Scan(OpenTabs, ActiveTab, Content);
        var failed  = new List<string>();

        foreach (var item in pending)
        {
            try
            {
                _fileService.WriteFile(item.FilePath, item.Content);
                MarkSaved(item);
            }
            catch (Exception ex)
            {
                failed.Add($"{item.Tab.FileName}: {ex.Message}");
            }
        }

        return failed;
    }

    /// <summary>
    /// Limpia el estado sucio tras una escritura exitosa, y SOLO si el texto no cambió mientras se
    /// escribía (ver <see cref="DirtyTabScanner.CanClearDirty"/>).
    ///
    /// La bandera del PANEL (<see cref="IsDirty"/>) es la de la pestaña activa: solo se baja cuando
    /// fue ESA la que se guardó, si no el título diría "guardado" con la pestaña de adelante
    /// todavía modificada.
    /// </summary>
    private void MarkSaved(PendingTabSave item)
    {
        var current = item.Source == TabContentSource.LiveEditor ? Content : item.Tab.Content;
        if (!DirtyTabScanner.CanClearDirty(current, item.Content)) return;

        item.Tab.IsDirty = false;
        if (item.Source == TabContentSource.LiveEditor) IsDirty = false;
    }

    /// <summary>
    /// Un solo mensaje por pasada, no uno por pestaña: el guardado automático corre cada 30 s y
    /// un modal por archivo fallido lo convertiría en una metralleta de diálogos.
    /// </summary>
    private void ReportSaveOutcome(int saved, IReadOnlyList<string> failed)
    {
        if (failed.Count > 0)
        {
            _dialogService.ShowError(
                "No se pudieron guardar estos documentos:\n\n" + string.Join("\n", failed),
                "Error al guardar");
        }

        if (saved == 0) return;

        StatusSink?.Invoke(saved == 1
            ? $"Guardado  {DateTime.Now:HH:mm:ss}"
            : $"Guardados {saved} documentos  {DateTime.Now:HH:mm:ss}");
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
        //
        // Vault-scoped resolution: render against THIS file's owning root, not the global
        // top root, so a note from vault B previewed while vault A is also open still gets
        // vault B's vault.local base href. Falls back to the top open root when the file
        // sits outside every open vault (e.g. an unsaved buffer) — same as legacy behavior.
        var vaultRoot = _fileService.GetOwningRoot(CurrentFilePath) ?? _fileService.VaultRoot;

        var ext = Path.GetExtension(CurrentFilePath).ToLowerInvariant();
        if (ext == ".html" || ext == ".htm")
        {
            // Raw HTML is a full document, not a fragment → force full navigation.
            PreviewBodyHtml = string.Empty;
            PreviewHtml     = _markdownService.PrepareHtmlForPreview(Content, vaultRoot);
        }
        else if (ext == ".mermaid" || ext == ".mmd")
        {
            var markdown = $"```mermaid\n{Content}\n```";
            PreviewBodyHtml = _markdownService.RenderBody(markdown);
            PreviewHtml     = _markdownService.RenderToHtml(markdown, IsDarkTheme, vaultRoot);
        }
        else if (Models.SupportedExtensions.LanguageFor(CurrentFilePath) is { } lang)
        {
            // Source code has no "rendered" form — wrap it in a fenced code block so the
            // syntax-highlight plugin colours it in the preview, same trick as Mermaid.
            var markdown = $"```{lang}\n{Content}\n```";
            PreviewBodyHtml = _markdownService.RenderBody(markdown);
            PreviewHtml     = _markdownService.RenderToHtml(markdown, IsDarkTheme, vaultRoot);
        }
        else
        {
            PreviewBodyHtml = _markdownService.RenderBody(Content);
            PreviewHtml     = _markdownService.RenderToHtml(Content, IsDarkTheme, vaultRoot);
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

    private void RebuildPluginToolbar()
    {
        PluginToolbarItems.Clear();

        // Se pasa la FÁBRICA, no un contexto ya construido: el contexto nace en el clic para
        // poder fijar la pestaña activa de ese instante (ver CreatePluginEditorContext).
        // El portón es el mismo que el de la barra propia: sin pestaña abierta, un comando de
        // plugin no tiene destino y su escritura degrada (PinnedEditorContext → NoDocument).
        // Se pasa la FUNCIÓN, no el valor: la barra se construye una vez y el estado cambia
        // muchas veces después.
        foreach (var group in _registry.CommandGroups)
            PluginToolbarItems.Add(PluginToolbarItemViewModel.Group(group, CreatePluginEditorContext, () => HasOpenDocument));
        foreach (var command in _registry.Commands)
            PluginToolbarItems.Add(PluginToolbarItemViewModel.Single(command, CreatePluginEditorContext, () => HasOpenDocument));
    }

    /// <summary>
    /// Crea el <see cref="IEditorContext"/> de UNA invocación de comando, fijando la pestaña
    /// activa AHORA. Antes había un único adapter cacheado por panel y compartido por todos
    /// los items de la barra: un plugin que retenía el contexto y escribía más tarde (dictado
    /// en vivo, transcripción de archivo) insertaba en la pestaña que estuviera activa EN ESE
    /// MOMENTO, no en la que el usuario tenía delante al apretar el botón. Ver
    /// <see cref="PinnedEditorContext"/> para el enrutado completo.
    /// </summary>
    internal IEditorContext CreatePluginEditorContext() => new PinnedEditorContext(this, ActiveTab);

    /// <summary>
    /// Workbench-owned lookup: qué grupo tiene abierta ESTA pestaña ahora mismo. Hace falta
    /// porque una <see cref="OpenTab"/> puede MIGRAR de panel («Mover al otro panel», salir del
    /// split) sin cerrarse — buscarla solo en este grupo la daría por cerrada y degradaría una
    /// operación que en realidad sigue siendo perfectamente válida.
    /// Null en tests → el grupo se comporta standalone (modo panel único), misma convención que
    /// <see cref="RedirectIfOwnedElsewhere"/>.
    /// </summary>
    internal Func<OpenTab, EditorGroupViewModel?>? OwnerOfTab { get; set; }

    /// <summary>Grupo que tiene abierta <paramref name="tab"/>, o null si ya no está abierta en ninguno.</summary>
    internal EditorGroupViewModel? ResolveOwner(OpenTab tab)
    {
        if (OwnerOfTab is { } lookup) return lookup(tab);
        return OpenTabs.Contains(tab) ? this : null;
    }

    // Puentes que usa PinnedEditorContext (traducen a los eventos que maneja la View).
    internal string PluginGetSelectedText()               => SelectedTextProvider?.Invoke() ?? string.Empty;
    internal void   PluginInsertAtCaret(string text)      => SnippetRequested?.Invoke(text);
    internal void   PluginWrapSelection(string b, string a) => InsertionRequested?.Invoke(b, a);
    internal void   PluginReplaceSelection(string text)   => ReplaceSelectionRequested?.Invoke(text);

    // TODOS los comandos de inserción exigen documento abierto. Poner el área de texto en
    // solo-lectura NO alcanza: IsReadOnly de AvalonEdit frena el tipeo del usuario, pero estos
    // comandos terminan en Document.Insert desde la vista, que pasa por encima del provider de
    // solo-lectura y volvería a escribir en el vacío.
    [RelayCommand(CanExecute = nameof(HasOpenDocument))] private void InsertBold()    => InsertionRequested?.Invoke("**", "**");
    [RelayCommand(CanExecute = nameof(HasOpenDocument))] private void InsertItalic()  => InsertionRequested?.Invoke("*", "*");
    [RelayCommand(CanExecute = nameof(HasOpenDocument))] private void InsertCode()    => InsertionRequested?.Invoke("`", "`");

    /// <summary>Inserts a fenced code block with the given language tag (e.g. "csharp", "sql").</summary>
    [RelayCommand(CanExecute = nameof(HasOpenDocument))]
    private void InsertCodeBlock(string language) =>
        InsertionRequested?.Invoke($"```{language}\n", "\n```");

    // NOTE: "Insertar ejemplo Mermaid" se migró al plugin Mermaid (aporta su propio
    // dropdown vía PluginCommandGroup). Al desactivar el plugin, su menú desaparece.

    [RelayCommand(CanExecute = nameof(HasOpenDocument))] private void InsertHeading1() => InsertionRequested?.Invoke("# ", "");
    [RelayCommand(CanExecute = nameof(HasOpenDocument))] private void InsertHeading2() => InsertionRequested?.Invoke("## ", "");
    [RelayCommand(CanExecute = nameof(HasOpenDocument))] private void InsertHeading3() => InsertionRequested?.Invoke("### ", "");
    [RelayCommand(CanExecute = nameof(HasOpenDocument))] private void InsertBulletList()   => InsertionRequested?.Invoke("- ", "");
    [RelayCommand(CanExecute = nameof(HasOpenDocument))] private void InsertNumberedList() => InsertionRequested?.Invoke("1. ", "");
    [RelayCommand(CanExecute = nameof(HasOpenDocument))] private void InsertLink()    => InsertionRequested?.Invoke("[", "](url)");

    [RelayCommand(CanExecute = nameof(HasOpenDocument))]
    private void InsertInternalLink()
    {
        // Vault-scoped resolution: the link picker only offers notes from THIS tab's
        // own owning vault, never files from another open vault. Falls back to the top
        // open root when the file sits outside every open vault (unsaved buffer, same
        // legacy behavior as RefreshPreview above).
        var owningRoot = _fileService.GetOwningRoot(CurrentFilePath) ?? _fileService.VaultRoot;
        var files = _fileService.GetVaultFiles(owningRoot ?? string.Empty);
        if (files.Count == 0)
        {
            _dialogService.ShowInfo(
                "No hay archivos en el vault. Creá un archivo primero.",
                "Vault vacío");
            return;
        }

        var markdown = _dialogService.PickInternalLinkMarkdown(
            files, CurrentFilePath, owningRoot ?? string.Empty);
        if (markdown is null) return;

        InsertionRequested?.Invoke(markdown, "");
    }

    [RelayCommand(CanExecute = nameof(HasOpenDocument))]
    private void InsertImage()
    {
        var imagePath = _dialogService.AskImagePath();
        if (imagePath is null) return;

        try
        {
            // Vault-scoped resolution: pasted images land in the CURRENT tab's own
            // vault assets/, not the top open root, so a vault-B note keeps its images
            // in vault B even while vault A is also open.
            var owningRoot = _fileService.GetOwningRoot(CurrentFilePath) ?? _fileService.VaultRoot;
            var fallback = HasFile ? Path.GetDirectoryName(CurrentFilePath) : null;
            var destPath = _fileService.CopyImageToAssets(owningRoot, imagePath, fallback);
            var md       = _fileService.BuildImageMarkdown(owningRoot, destPath, "image");
            InsertionRequested?.Invoke(md, "");
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(ex.Message, "Error de imagen");
        }
    }

    /// <summary>
    /// Guarda una imagen pegada desde el portapapeles y devuelve su referencia Markdown, o
    /// <c>null</c> si falló (al usuario ya se le avisó).
    ///
    /// Resolución por vault, igual que <see cref="InsertImage"/> y <see cref="RefreshPreview"/>:
    /// la imagen aterriza en el <c>attachments/</c> del vault DUEÑO de la nota activa, no en el
    /// del primer vault abierto. Con dos vaults abiertos y la nota en el segundo, calcular
    /// contra <c>VaultRoot</c> escribía el PNG en el vault A: el enlace <c>attachments/...</c>
    /// quedaba bien formado pero la vista previa —que mapea <c>vault.local</c> a la raíz
    /// dueña— lo buscaba en el vault B y no encontraba nada. Imagen rota, archivo en el vault
    /// equivocado.
    /// </summary>
    public string? SavePastedImage(byte[] pngBytes)
    {
        var owningRoot = _fileService.GetOwningRoot(CurrentFilePath) ?? _fileService.VaultRoot;
        var fallback   = HasFile ? Path.GetDirectoryName(CurrentFilePath) : null;

        try
        {
            var destPath = _fileService.SaveImageToAttachments(owningRoot, pngBytes, fallback);
            // El enlace se calcula contra la MISMA base en la que se escribió el archivo:
            // si no hubo raíz y se usó el fallback, relativizar contra la raíz (null) daría
            // solo el nombre y el enlace apuntaría al lugar equivocado.
            return _fileService.BuildImageMarkdown(owningRoot ?? fallback, destPath, "screenshot");
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(ex.Message, "Error al pegar la imagen");
            return null;
        }
    }

    // ─── Drag & drop ─────────────────────────────────────────────────────────

    /// <summary>
    /// Handles image files dropped onto the editor.
    ///
    /// Soltar no pasa por ningún <c>CanExecute</c> —la vista llama a este método directo—, así
    /// que el portón va acá: sin pestaña abierta la imagen se copiaría a <c>assets/</c> y su
    /// Markdown se insertaría en un control que nadie va a guardar. Se avisa en vez de callar:
    /// el usuario acaba de arrastrar un archivo y espera VER algo.
    /// </summary>
    public void HandleDroppedFiles(string[] paths)
    {
        if (!HasOpenDocument)
        {
            StatusSink?.Invoke("No hay ningún documento abierto: abrí o creá un archivo antes de soltar imágenes.");
            return;
        }

        var imageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg" };

        // Vault-scoped resolution: same as InsertImage — dropped images go to the
        // CURRENT tab's own owning vault, not the top open root.
        var owningRoot = _fileService.GetOwningRoot(CurrentFilePath) ?? _fileService.VaultRoot;
        var fallback = HasFile ? Path.GetDirectoryName(CurrentFilePath) : null;
        foreach (var path in paths)
        {
            if (!imageExts.Contains(Path.GetExtension(path))) continue;
            try
            {
                var destPath = _fileService.CopyImageToAssets(owningRoot, path, fallback);
                InsertionRequested?.Invoke(_fileService.BuildImageMarkdown(owningRoot, destPath, "image"), "");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError(ex.Message, "Error al soltar");
            }
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static int CountWords(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
}
