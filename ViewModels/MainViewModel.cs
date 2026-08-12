using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkdownVault.Models;
using MarkdownVault.Services;
using MarkdownVault.Services.Plugins;

namespace MarkdownVault.ViewModels;

/// <summary>Root ViewModel that orchestrates the file tree, editor group(s), and global settings.</summary>
public partial class MainViewModel : ObservableObject
{
    private readonly FileService     _fileService;
    private readonly MarkdownService _markdownService;
    private readonly PluginRegistry  _pluginRegistry;
    private readonly IDialogService  _dialogService;
    private readonly SettingsService _settingsService;
    private          AppSettings     _settings;

    // See EditorGroupViewModel's identical field — same reasoning (testability: xUnit has
    // no Application.Current, so the production default would silently swallow the callback).
    private readonly Action<Action> _uiDispatch;

    public FileTreeViewModel FileTree { get; }

    /// <summary>Editor panes ("groups"). Exactly one in Phase 1-3; a second is added in Phase 4.</summary>
    public ObservableCollection<EditorGroupViewModel> Groups { get; } = new();

    /// <summary>The group currently receiving focus. Never null — always a member of <see cref="Groups"/>.</summary>
    public EditorGroupViewModel FocusedGroup { get; private set; } = null!;

    /// <summary>
    /// Backing state for the Obsidian-style graph view (nodes, filters, forces). Promoted from
    /// EditorGroupViewModel in Phase 2 — vault-wide, not per-pane (design §2.1/Decision 2).
    /// </summary>
    public GraphViewModel Graph { get; }

    public MainViewModel(
        FileService                    fileService,
        MarkdownService                markdownService,
        SettingsService                settingsService,
        Services.Plugins.PluginRegistry pluginRegistry,
        IDialogService                 dialogService,
        Action<Action>?                uiDispatch = null)
    {
        _uiDispatch = uiDispatch ??
            (a => { if (Application.Current is { } app) app.Dispatcher.Invoke(a); else a(); });

        // Stored so CreateGroup() can build pane B later (EnterSplit, or a split restored
        // from settings) without re-threading ctor parameters through every call site.
        _fileService     = fileService;
        _markdownService = markdownService;
        _pluginRegistry  = pluginRegistry;
        _dialogService   = dialogService;
        _settingsService = settingsService;

        // Seed the workbench with exactly one group, as the ctor's first statements and
        // before any settings are applied below (design §5.1's ctor-ordering invariant —
        // FocusedGroup must be non-null from birth).
        Groups.Add(CreateGroup());
        FocusedGroup = Groups[0];

        _settings = settingsService.Load();

        // Graph is workbench-wide (Phase 2/Decision 2): one instance shared by every pane.
        Graph = new GraphViewModel(new GraphService(fileService));
        Graph.FileOpenRequested += async path =>
        {
            ShowGraph = false;
            await OpenInFocusedGroupAsync(path);
        };

        // Single external-change subscription for the whole workbench (Phase 3 / design §5.5) —
        // one lookup to find the owning group, instead of every group subscribing separately.
        _fileService.FileChangedExternally += (_, e) =>
            _uiDispatch(() => HandleExternalChange(e.FullPath));

        FileTree = new FileTreeViewModel(fileService);

        // Wire file-open requests from the tree through the path-uniqueness invariant
        // (Phase 3, HARD GATE): if the path is already open in another group, that group is
        // focused instead of opening a duplicate.
        FileTree.FileOpenRequested += async path => await OpenInFocusedGroupAsync(path);

        // Apply persisted settings.
        ViewMode = _settings.ViewMode;
        foreach (var group in Groups)
            group.ConfigureAutoSave(_settings.AutoSaveEnabled, _settings.AutoSaveIntervalSec);
        IsDarkTheme = _settings.IsDarkTheme;
        foreach (var group in Groups)
            group.IsDarkTheme = IsDarkTheme;
        ApplyTheme(IsDarkTheme);
        IsExplorerVisible = _settings.IsExplorerVisible;
        FontFamily  = _settings.FontFamily;
        FontSize    = _settings.FontSize;
        PreviewZoom = _settings.PreviewZoom;

        // Split geometry persists, open documents do not (SE-3). This is a restore, not a
        // live split gesture, so SE-1's "focus moves to B" does NOT apply here — SE-9's "App
        // start: FocusedGroup is pane A" wins regardless of whether split is restored.
        SplitRatio = _settings.SplitRatio;
        if (_settings.IsSplit)
        {
            Groups.Add(CreateGroup());
            IsSplit = true;
        }

        // Restore last vault if it still exists.
        if (!string.IsNullOrWhiteSpace(_settings.LastVaultPath) &&
            System.IO.Directory.Exists(_settings.LastVaultPath))
        {
            OpenVaultPath(_settings.LastVaultPath);
        }
    }

    /// <summary>
    /// Constructs a group and wires the workbench-owned delegates it needs (Phase 2's
    /// StatusSink, Phase 3's RedirectIfOwnedElsewhere + graph sync, Phase 4's FocusRequested +
    /// auto-collapse) — the single place both the initial group and every later pane B
    /// (EnterSplit, or a split restored from settings) go through.
    /// </summary>
    private EditorGroupViewModel CreateGroup()
    {
        var group = new EditorGroupViewModel(_fileService, _markdownService, _pluginRegistry, _dialogService);

        group.StatusSink = msg => StatusMessage = msg;

        // Path-uniqueness invariant (Phase 3, HARD GATE): if another group already owns the
        // path, focus it and switch to its tab instead of letting this group open a duplicate.
        group.RedirectIfOwnedElsewhere = path =>
        {
            var owner = FindOwner(path);
            if (owner is null || owner == group) return false;
            SetFocus(owner);
            owner.SwitchToTabCommand.Execute(owner.Find(path));
            return true;
        };

        // Keep the graph's active-file highlight in sync, but only for the focused group —
        // a background pane switching tabs shouldn't move the highlight (design §2.2). Also
        // watch for this group's tab collection going empty: SE-4's auto-collapse fires only
        // for a non-primary group while split is active (CollapseGroup no-ops otherwise).
        group.ActiveTabChanged += tab =>
        {
            if (group == FocusedGroup) SyncGraphActiveFile();
            // "Comparar archivos" depende de que AMBOS paneles tengan pestaña activa;
            // reevaluar al abrir/cerrar/cambiar de pestaña en cualquiera de los dos.
            CompareFilesCommand.NotifyCanExecuteChanged();
            if (tab is null && group.OpenTabs.Count == 0) CollapseGroup(group);
        };

        // Phase 4 focus tracking: the View reports focus on the group, the group forwards it
        // here — the group never reaches up to MainViewModel directly.
        group.FocusRequested += SetFocus;

        return group;
    }

    /// <summary>
    /// Re-renders every pane's preview after the active plugin set changes (design §2.1/App
    /// startup). The plugin shell changed for both panes, not just the focused one — a single
    /// group's <see cref="EditorGroupViewModel.RefreshPreviewFromPlugins"/> is not enough.
    /// </summary>
    public void RefreshPreviewFromPluginsAllGroups()
    {
        foreach (var group in Groups) group.RefreshPreviewFromPlugins();
    }

    // ─── Observable properties ───────────────────────────────────────────────

    [ObservableProperty] private bool   _isDarkTheme;
    [ObservableProperty] private bool   _isExplorerVisible = true;

    partial void OnIsDarkThemeChanged(bool value)
    {
        foreach (var group in Groups) group.IsDarkTheme = value;
    }

    // ─── View mode (promoted from EditorGroupViewModel, Phase 2 / D1) ─────────

    [ObservableProperty] private ViewMode _viewMode = ViewMode.EditAndPreview;

    partial void OnViewModeChanged(ViewMode value) =>
        OnPropertyChanged(nameof(ShowPreviewPanel));

    public bool ShowPreviewPanel => ViewMode != ViewMode.EditorOnly;

    [RelayCommand]
    private void SetViewMode(ViewMode mode) => ViewMode = mode;

    // Dead code — bound nowhere in XAML (design C9). Moved verbatim; not this change's job
    // to invent a binding or delete it.
    [RelayCommand]
    private void CycleViewMode() => ViewMode = ViewMode switch
    {
        ViewMode.EditorOnly     => ViewMode.EditAndPreview,
        ViewMode.EditAndPreview => ViewMode.ViewerOnly,
        _                       => ViewMode.EditorOnly
    };

    // ─── Status bar (promoted from EditorGroupViewModel, Phase 2) ─────────────

    [ObservableProperty] private string _statusMessage = "Listo";

    // ─── Graph (promoted from EditorGroupViewModel, Phase 2 / Decision 2) ─────

    /// <summary>When true, the graph view replaces the editor+preview content area.</summary>
    [ObservableProperty] private bool _showGraph;

    partial void OnShowGraphChanged(bool value)
    {
        if (!value) return;
        SyncGraphActiveFile();
        _ = Graph.BuildAsync();
    }

    [RelayCommand]
    private void ToggleGraph() => ShowGraph = !ShowGraph;

    /// <summary>Pushes the focused group's active file (vault-relative path) to the graph for highlighting.</summary>
    private void SyncGraphActiveFile()
    {
        var root = _fileService.VaultRoot;
        Graph.ActiveFile = (FocusedGroup.ActiveTab is not null && !string.IsNullOrEmpty(root))
            ? System.IO.Path.GetRelativePath(root, FocusedGroup.ActiveTab.FilePath).Replace('\\', '/')
            : string.Empty;
    }

    // ─── Path-uniqueness invariant (Phase 3, HARD GATE) ────────────────────────
    // One enforcement point. Every open entry point (file-tree, internal-link nav via the
    // group's own OpenFileAsync + RedirectIfOwnedElsewhere, plugin OpenFileAction, graph node
    // click) routes through here so a path can never be open in two groups at once.

    /// <summary>Returns the group that already has <paramref name="path"/> open, or null.</summary>
    internal EditorGroupViewModel? FindOwner(string path) =>
        Groups.FirstOrDefault(g => g.Owns(path));

    /// <summary>
    /// Opens <paramref name="path"/> in <paramref name="target"/>, unless another group already
    /// owns it — in which case that group is focused and switched to the existing tab instead
    /// of creating a duplicate.
    /// </summary>
    public async Task OpenInGroupAsync(EditorGroupViewModel target, string path)
    {
        if (FindOwner(path) is { } owner)
        {
            SetFocus(owner);
            owner.SwitchToTabCommand.Execute(owner.Find(path));
            return;
        }
        await target.OpenFileAsync(path);
    }

    public Task OpenInFocusedGroupAsync(string path) => OpenInGroupAsync(FocusedGroup, path);

    /// <summary>
    /// Moves an open tab from whichever group owns it into the other group (Phase 4's "Mover
    /// al otro panel"). No disk re-read — the <see cref="OpenTab"/> instance itself moves, so
    /// content/dirty/caret/scroll all survive. A no-op until a second pane exists.
    /// </summary>
    internal void MoveTabToOtherGroup(OpenTab tab)
    {
        var source = Groups.FirstOrDefault(g => g.OpenTabs.Contains(tab));
        var target = Groups.FirstOrDefault(g => g != source);
        if (source is null || target is null) return;

        var wasActive   = tab == source.ActiveTab;
        var sourceIndex = source.OpenTabs.IndexOf(tab);
        source.OpenTabs.Remove(tab);

        if (wasActive)
        {
            if (source.OpenTabs.Count == 0)
                source.ActiveTab = null;
            else
                source.SwitchToTabCommand.Execute(
                    source.OpenTabs[Math.Min(sourceIndex, source.OpenTabs.Count - 1)]);
        }

        target.OpenTabs.Add(tab);
        target.SwitchToTabCommand.Execute(tab);
        SetFocus(target);
    }

    /// <summary>Tab context-menu wrapper for <see cref="MoveTabToOtherGroup"/> (SE-12). The menu
    /// item itself is only visible/enabled when <see cref="IsSplit"/> — there is no other pane
    /// to move to otherwise.</summary>
    [RelayCommand]
    private void MoveTabToOtherPanel(OpenTab? tab)
    {
        if (tab is not null) MoveTabToOtherGroup(tab);
    }

    /// <summary>
    /// Never-null focus transition: rejects null, any group not in <see cref="Groups"/>, and
    /// no-ops when <paramref name="g"/> is already focused (design §5.1) — cheap, since
    /// <see cref="EditorGroupViewModel.NotifyFocused"/> fires on every mouse press in a pane,
    /// including repeated presses in the pane already focused. Raises the notification for
    /// <see cref="FocusedGroup"/> manually because it is a plain get-only property, not an
    /// <c>[ObservableProperty]</c> — every <c>FocusedGroup.*</c> XAML binding (title,
    /// keybindings, status bar) depends on this firing to re-resolve against the newly
    /// focused group. (Phase 5 removed the transitional <c>Editor</c> facade this used to
    /// also notify — <c>FocusedGroup</c> is now the only binding path.)
    /// </summary>
    internal void SetFocus(EditorGroupViewModel? g)
    {
        if (g is null || !Groups.Contains(g) || ReferenceEquals(g, FocusedGroup)) return;
        FocusedGroup = g;
        OnPropertyChanged(nameof(FocusedGroup));
        SyncGraphActiveFile();
    }

    // ─── Split (Phase 4, first user-visible change) ────────────────────────────────────────────

    /// <summary>Persisted split geometry (SE-3). <see cref="Groups"/>.Count is the actual
    /// source of truth for whether pane B exists; this bool mirrors it for XAML and settings.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompareFilesCommand))]
    private bool   _isSplit;

    /// <summary>Pane A's share of the nested editor-region grid (0..1). Captured imperatively
    /// from the GridSplitter's drag, same as the existing <c>_lastExplorerWidth</c> trick —
    /// read in the ctor, written in SaveSettings, both ends wired (design C8's warning about
    /// FileTreeWidth/EditorColumnWidth being declared-and-dead applies directly here).</summary>
    [ObservableProperty] private double _splitRatio = 0.5;

    [RelayCommand]
    private void ToggleSplit()
    {
        if (IsSplit) ExitSplit();
        else EnterSplit();
    }

    /// <summary>
    /// SE-1 (confirmed decision, engram <c>architecture/split-editor-decisions</c>): creates
    /// pane B empty and MOVES focus to it. Splitting is a statement of intent — if focus stayed
    /// on A, the next file opened from the tree would land in A and B would stay empty forever
    /// short of a manual "Mover al otro panel".
    /// </summary>
    internal void EnterSplit()
    {
        if (IsSplit) return;
        var groupB = CreateGroup();
        Groups.Add(groupB);
        IsSplit = true;
        SetFocus(groupB);
        SaveSettings();
    }

    /// <summary>
    /// SE-2 (confirmed default): merges pane B's open tabs into pane A, preserving each tab's
    /// dirty/clean state and content, before B is detached. Pane A's own previously-active tab
    /// stays active — never B's — unless A had none to begin with (its last tab could have been
    /// closed while split was active, SE-5), in which case B's former active tab is promoted so
    /// the merged strip doesn't end up with tabs but no active one.
    /// </summary>
    internal void ExitSplit()
    {
        if (!IsSplit || Groups.Count < 2) { IsSplit = false; return; }

        var groupA          = Groups[0];
        var groupB          = Groups[1];
        var groupBActiveTab = groupB.ActiveTab;

        foreach (var tab in groupB.OpenTabs.ToList())
        {
            groupB.OpenTabs.Remove(tab);
            tab.IsActive = false;   // exactly one active tab across the merged single strip
            groupA.OpenTabs.Add(tab);
        }

        if (groupA.ActiveTab is null && groupBActiveTab is not null)
        {
            groupBActiveTab.IsActive = true;   // the loop above cleared it along with every other migrated tab
            groupA.ActiveTab         = groupBActiveTab;
        }

        SetFocus(groupA);
        Groups.Remove(groupB);
        IsSplit = false;
        SaveSettings();
    }

    /// <summary>
    /// SE-4: auto-collapses a non-primary group when its last tab closes, moving focus back to
    /// pane A. SE-5: pane A (<c>Groups[0]</c>) never auto-collapses — checked first, so pane A
    /// reaching zero tabs while split is active is a no-op here, same path as an explicit
    /// manual unsplit (<see cref="ExitSplit"/>) minus the merge (there is nothing to merge; the
    /// collapsing group has zero tabs by definition).
    /// </summary>
    private void CollapseGroup(EditorGroupViewModel group)
    {
        if (!IsSplit || !Groups.Contains(group) || group == Groups[0]) return;
        SetFocus(Groups[0]);
        Groups.Remove(group);
        IsSplit = false;
        SaveSettings();
    }

    // ─── Compare files (side-by-side diff, split-only) ─────────────────────────

    /// <summary>
    /// Inyectada en composición (App.xaml.cs) para crear la superficie de comparación
    /// real (una <c>Window</c> WebView2, no construible bajo xUnit headless). Null en
    /// tests que no ejercitan la presentación: <see cref="CompareFiles"/> igual computa el
    /// diff. Los tests que sí quieren ejercitar el merge inyectan un doble en memoria.
    /// </summary>
    internal Func<ICompareView>? CompareViewFactory { get; set; }

    // Sesión de comparación viva: los dos paneles enfrentados y el diff vigente, para
    // poder aplicar un merge y re-renderizar sin recomputar quién compara con quién.
    private ICompareView?           _compareView;
    private EditorGroupViewModel?   _compareLeft;
    private EditorGroupViewModel?   _compareRight;
    private IReadOnlyList<DiffRow>? _compareRows;

    /// <summary>
    /// Solo tiene sentido con el editor dividido y ambos paneles con un archivo activo:
    /// no hay dos archivos que comparar en ningún otro caso. Reevaluado cuando cambia
    /// <see cref="IsSplit"/> (atributo) y cuando cambia la pestaña activa de cualquier
    /// panel (<see cref="CreateGroup"/> lo notifica en <c>ActiveTabChanged</c>).
    /// </summary>
    private bool CanCompareFiles() =>
        IsSplit && Groups.Count >= 2 &&
        Groups[0].ActiveTab is not null && Groups[1].ActiveTab is not null;

    /// <summary>
    /// Abre (o refresca) la comparación lado-a-lado estilo Beyond Compare del archivo
    /// activo de cada panel, sobre su contenido EN MEMORIA (incluye ediciones sin guardar).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCompareFiles))]
    private void CompareFiles()
    {
        if (!CanCompareFiles()) return;

        _compareLeft  = Groups[0];
        _compareRight = Groups[1];
        _compareRows  = new DiffService().Diff(_compareLeft.Content, _compareRight.Content);
        var html = RenderCompareHtml();

        // Ya hay una ventana abierta → refrescarla en vez de abrir otra.
        if (_compareView is not null) { _compareView.Reload(html); return; }

        var view = CompareViewFactory?.Invoke();
        if (view is null) return;   // sin factory (tests de solo cómputo) → no-op de presentación

        _compareView = view;
        view.MergeRequested += OnCompareMergeRequested;
        view.ViewClosed     += OnCompareClosed;
        view.Show(html, IsDarkTheme, CompareTitle());
    }

    /// <summary>
    /// Copia una línea —o el bloque de diferencias entero (◀◀/▶▶)— al otro archivo: aplica
    /// el merge PURO al contenido de cada panel, lo que marca dirty el lado tocado (el setter
    /// generado hace no-op si no cambió), recomputa el diff y refresca la vista. El usuario
    /// guarda cuando quiere (Ctrl+S).
    /// </summary>
    internal void OnCompareMergeRequested(CompareMergeRequest req)
    {
        if (_compareLeft is null || _compareRight is null || _compareRows is null) return;

        var (newLeft, newRight) = req.WholeBlock
            ? DiffMerge.ApplyBlock(_compareRows, req.Row, req.Direction, _compareLeft.Content, _compareRight.Content)
            : DiffMerge.Apply     (_compareRows, req.Row, req.Direction, _compareLeft.Content, _compareRight.Content);

        _compareLeft.Content  = newLeft;
        _compareRight.Content = newRight;

        _compareRows = new DiffService().Diff(newLeft, newRight);
        _compareView?.Reload(RenderCompareHtml());
    }

    private void OnCompareClosed()
    {
        if (_compareView is not null)
        {
            _compareView.MergeRequested -= OnCompareMergeRequested;
            _compareView.ViewClosed     -= OnCompareClosed;
        }
        _compareView  = null;
        _compareLeft  = null;
        _compareRight = null;
        _compareRows  = null;
    }

    private string CompareTitle() =>
        $"Comparar: {_compareLeft!.ActiveTab?.FileName} ↔ {_compareRight!.ActiveTab?.FileName}";

    private string RenderCompareHtml()
    {
        // Resaltado de sintaxis por lado según la extensión del archivo activo (reutiliza las
        // definiciones de AvalonEdit; degrada a texto plano si el lenguaje no está soportado).
        var leftSyntax  = new SyntaxHighlighter(_compareLeft!.Content,  ExtensionOf(_compareLeft));
        var rightSyntax = new SyntaxHighlighter(_compareRight!.Content, ExtensionOf(_compareRight));

        return DiffHtmlRenderer.Render(
            _compareRows!, _compareLeft.ActiveTab?.FileName ?? "A",
            _compareRight.ActiveTab?.FileName ?? "B", IsDarkTheme, leftSyntax, rightSyntax);
    }

    private static string? ExtensionOf(EditorGroupViewModel group) =>
        group.ActiveTab is { FilePath: var p } && !string.IsNullOrEmpty(p)
            ? System.IO.Path.GetExtension(p)
            : null;

    // ─── External change handling (promoted from EditorGroupViewModel, Phase 3) ───────────────

    /// <summary>
    /// A file changed on disk outside the app. With the uniqueness invariant this is a single
    /// lookup, not a loop over every group — at most one group can own the path.
    /// </summary>
    private void HandleExternalChange(string fullPath)
    {
        if (FindOwner(fullPath) is { } owner)
            _ = owner.ReloadIfCleanAsync(fullPath);
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
        // Independent-vault switch: files from the previous vault don't belong to the
        // new one, so close their tabs before swapping the root. No-op on startup and
        // on the first open (no tabs yet). ToList() snapshot: closing pane B's last tab
        // can trigger CollapseGroup, which mutates Groups mid-loop (SE-4) — enumerating
        // the live collection here would throw InvalidOperationException.
        foreach (var group in Groups.ToList())
            group.CloseAllTabsCommand.Execute(null);

        _fileService.OpenVault(path);
        FileTree.LoadVault(path);
        _settings.LastVaultPath = path;
        RegisterKnownVault(path);
        OnPropertyChanged(nameof(VaultName));
        SaveSettings();
    }

    /// <summary>Adds a vault root to the known list (deduplicated, case-insensitive).</summary>
    private void RegisterKnownVault(string path)
    {
        if (!_settings.KnownVaultPaths.Any(
                p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
            _settings.KnownVaultPaths.Add(path);
    }

    /// <summary>
    /// Builds the VM behind the "Administrar vaults" form, wired to the live vault
    /// registry, the real folder picker, and this VM's open/persist operations.
    /// </summary>
    public VaultsViewModel CreateVaultsViewModel() =>
        new(
            knownPaths: _settings.KnownVaultPaths,
            activePath: _fileService.VaultRoot,
            pickFolder: PickVaultFolder,
            openVault:  OpenVaultPath,
            persist:    SaveSettings);

    private static string? PickVaultFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Seleccionar carpeta raíz del vault"
        };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    private void ApplyTheme(bool dark)
    {
        // Application.Current is null under xUnit (no WPF Application running) — the ctor
        // calls this unconditionally, so MainViewModel would be unconstructable in tests
        // without this guard. No-op is correct there; production always has an Application.
        if (Application.Current is not { } app) return;

        var dicts = app.Resources.MergedDictionaries;
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
        _settings.ViewMode            = ViewMode;
        _settings.AutoSaveEnabled     = true;
        _settings.AutoSaveIntervalSec = 30;
        _settings.IsSplit             = IsSplit;
        _settings.SplitRatio          = SplitRatio;
        _settingsService.Save(_settings);
    }
}
