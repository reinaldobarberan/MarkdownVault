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
        MigrateVaultPathsIfNeeded();

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

        // Restore every vault root left open at last exit (multi-vault workspace).
        // Uses AddRoot directly, same call pair OpenVaultPath now makes (Phase 5 dropped
        // its old single-vault switch-and-close-tabs dance), so every entry opens as its
        // own section instead of only the last one surviving.
        foreach (var root in _settings.OpenVaultPaths)
        {
            if (System.IO.Directory.Exists(root))
            {
                _fileService.AddRoot(root);
                FileTree.AddRoot(root);
            }
        }
    }

    /// <summary>
    /// One-time migration (spec "One-Time Settings Migration"): seeds the new
    /// <see cref="AppSettings.OpenVaultPaths"/> from the legacy single-vault
    /// <see cref="AppSettings.LastVaultPath"/>, guarded by <see cref="AppSettings.VaultPathsMigrated"/>
    /// so it only ever runs once — a vault the user later closes deliberately must not be
    /// resurrected by re-seeding on a subsequent launch.
    /// </summary>
    private void MigrateVaultPathsIfNeeded()
    {
        if (_settings.VaultPathsMigrated) return;

        if (_settings.OpenVaultPaths.Count == 0 && !string.IsNullOrWhiteSpace(_settings.LastVaultPath))
            _settings.OpenVaultPaths.Add(_settings.LastVaultPath);

        _settings.VaultPathsMigrated = true;
        _settingsService.Save(_settings);
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

        // Plugin writes are pinned to the tab that was active when the command ran, and a tab
        // can MIGRATE between panes without closing ("Mover al otro panel", ExitSplit). This
        // lookup lets a pinned context follow the TAB instead of the pane, so a long-running
        // dictation survives the move instead of being reported as "closed".
        group.OwnerOfTab = tab => Groups.FirstOrDefault(g => g.OpenTabs.Contains(tab));

        // Keep the graph's active-file highlight in sync, but only for the focused group —
        // a background pane switching tabs shouldn't move the highlight (design §2.2). Also
        // watch for this group's tab collection going empty: SE-4's auto-collapse fires only
        // for a non-primary group while split is active (CollapseGroup no-ops otherwise).
        group.ActiveTabChanged += tab =>
        {
            if (group == FocusedGroup)
            {
                SyncGraphActiveFile();
                // Multi-vault: the focused pane's active tab drives VaultName (Phase 5.2) —
                // switching tabs within the SAME focused group can move to a different vault.
                OnPropertyChanged(nameof(VaultName));
            }
            // "Comparar archivos" depende de que AMBOS paneles tengan pestaña activa;
            // reevaluar al abrir/cerrar/cambiar de pestaña en cualquiera de los dos.
            CompareFilesCommand.NotifyCanExecuteChanged();
            if (tab is null && group.OpenTabs.Count == 0) CollapseGroup(group);
        };

        // Phase 4 focus tracking: the View reports focus on the group, the group forwards it
        // here — the group never reaches up to MainViewModel directly.
        group.FocusRequested += SetFocus;

        // Reveal internal-link targets in the file explorer. Fires ONLY on internal-link
        // navigation (not plain tab switches), so the tree never moves on its own otherwise.
        group.LinkNavigated += path => FileTree.RevealFile(path);

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

    // ─── Barra de progreso de plugins (SDK 1.3.0) ─────────────────────────────
    //
    // La barra de estado alcanzaba para "Guardado 20:14:03" y NO alcanzaba para
    // contarle al usuario que se están descargando 574 MB: letra chica, esquina
    // inferior derecha, nadie la mira. Esto es la superficie visible del canal
    // IProgressScope del SDK.
    //
    // Este VM no conoce el coordinador ni ningún tipo de plugin: recibe primitivos
    // por ShowProgress/HideProgress (ya en el hilo de UI) y devuelve la cancelación
    // por un delegate que se enchufa en App.xaml.cs — mismo patrón que
    // CompareViewFactory. Así todo esto es puro y testeable sin WPF ni plugins.

    [ObservableProperty] private bool   _isProgressVisible;
    [ObservableProperty] private string _progressTitle           = "";
    [ObservableProperty] private string _progressMessage         = "";
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private bool   _isProgressIndeterminate = true;

    /// <summary>"57 %" o vacío si el avance es indeterminado.</summary>
    [ObservableProperty] private string _progressPercentText = "";

    /// <summary>"+2 en segundo plano" cuando hay más de un trabajo largo a la vez.</summary>
    [ObservableProperty] private string _progressOthersText = "";

    /// <summary>
    /// "Cancelar" pasa a "Descartar" una vez que el usuario ya pidió cancelar. No es
    /// cosmética: le dice que la segunda pulsación saca la barra aunque el plugin no
    /// haya cortado todavía (es la salida de emergencia que reemplaza a un timeout).
    /// </summary>
    [ObservableProperty] private string _progressCancelLabel = "Cancelar";

    /// <summary>
    /// Lo enchufa App.xaml.cs contra <c>PluginProgressCoordinator.CancelOrDiscardActive</c>.
    /// Null en pruebas: el comando queda inerte, sin lanzar.
    /// </summary>
    public Action? CancelProgressRequested { get; set; }

    /// <summary>
    /// Pinta el trabajo largo que está al tope de la pila de scopes.
    /// <paramref name="percent"/> null ⇒ modo indeterminado. Se llama SIEMPRE desde
    /// el hilo de UI (el coordinador ya hizo el marshaling).
    /// </summary>
    public void ShowProgress(string title, string message, double? percent, int others, bool cancelling)
    {
        ProgressTitle           = title;
        ProgressMessage         = message;
        IsProgressIndeterminate = percent is null;
        ProgressPercent         = percent ?? 0;
        ProgressPercentText     = FormatPercent(percent);
        ProgressOthersText      = FormatOthers(others);
        ProgressCancelLabel     = cancelling ? "Descartar" : "Cancelar";
        IsProgressVisible       = true;
    }

    /// <summary>No queda ningún scope vivo: la barra desaparece.</summary>
    public void HideProgress()
    {
        IsProgressVisible   = false;
        ProgressTitle       = "";
        ProgressMessage     = "";
        ProgressPercentText = "";
        ProgressOthersText  = "";
        ProgressPercent     = 0;
        ProgressCancelLabel = "Cancelar";
    }

    /// <summary>Sin decimales: en una barra, "57 %" y "57,3 %" dicen lo mismo y el
    /// segundo tiembla. Invariante para que no dependa de la cultura del sistema.</summary>
    internal static string FormatPercent(double? percent) =>
        percent is { } p
            ? Math.Round(p).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " %"
            : "";

    internal static string FormatOthers(int others) =>
        others <= 0 ? "" : $"+{others} en segundo plano";

    [RelayCommand]
    private void CancelProgress() => CancelProgressRequested?.Invoke();

    // ─── Graph (promoted from EditorGroupViewModel, Phase 2 / Decision 2) ─────

    /// <summary>When true, the graph view replaces the editor+preview content area.</summary>
    [ObservableProperty] private bool _showGraph;

    partial void OnShowGraphChanged(bool value)
    {
        if (!value) return;
        var root = SyncGraphActiveFile();
        // Unconditional rebuild: the graph may be stale from edits made while it was
        // hidden, even when the focused tab's vault hasn't changed since it was last shown.
        _ = Graph.BuildAsync(root);
    }

    [RelayCommand]
    private void ToggleGraph() => ShowGraph = !ShowGraph;

    /// <summary>
    /// Pushes the focused group's active file (path relative to ITS OWNING root) to the graph
    /// for highlighting, and — while the graph pane is visible — rebuilds the graph itself when
    /// focus has moved to a note owned by a DIFFERENT vault than the one currently graphed
    /// (Phase 6 / D7: the graph follows the focused tab's vault, with no cross-vault nodes or
    /// edges — <see cref="GraphViewModel.BuildIfRootChangedAsync"/> no-ops on a same-vault tab
    /// switch). Returns the resolved owning root (or <c>null</c>) so <see cref="OnShowGraphChanged"/>
    /// can force an unconditional rebuild without re-resolving it.
    /// </summary>
    private string? SyncGraphActiveFile()
    {
        var activeTab = FocusedGroup.ActiveTab;
        var root      = activeTab is not null ? _fileService.GetOwningRoot(activeTab.FilePath) : null;
        Graph.ActiveFile = (activeTab is not null && root is not null)
            ? System.IO.Path.GetRelativePath(root, activeTab.FilePath).Replace('\\', '/')
            : string.Empty;

        if (ShowGraph)
            _ = Graph.BuildIfRootChangedAsync(root);

        return root;
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
        OnPropertyChanged(nameof(VaultName)); // Phase 5.2: VaultName tracks the focused group's vault
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

    /// <summary>
    /// Multi-vault label for the status bar / window title (Phase 5.2): with several roots
    /// open, a single global "vault name" no longer means anything on its own, so this
    /// prefers the FOCUSED tab's owning root — the vault the user is actually looking at —
    /// and only falls back to a workspace-wide summary when nothing is focused.
    /// </summary>
    public string VaultName
    {
        get
        {
            var owningRoot = FocusedGroup.ActiveTab is not null
                ? _fileService.GetOwningRoot(FocusedGroup.ActiveTab.FilePath)
                : null;
            if (owningRoot is not null)
                return RootDisplayName(owningRoot);

            return _fileService.VaultRoots.Count switch
            {
                0 => "Sin vault abierto",
                1 => RootDisplayName(_fileService.VaultRoots[0]),
                _ => $"{_fileService.VaultRoots.Count} vaults abiertos"
            };
        }
    }

    private static string RootDisplayName(string root) =>
        System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(root));

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

    // ─── Cierre con cambios sin guardar ──────────────────────────────────────
    // Hueco de PÉRDIDA DE DATOS: Window_Closing guardaba la configuración y cerraba, sin mirar
    // nunca si quedaban pestañas modificadas. CloseTab pregunta al cerrar UNA pestaña; cerrar la
    // APLICACIÓN no preguntaba nada. Con el guardado automático alcanzando solo a la pestaña
    // activa, un dictado largo hacia una pestaña de segundo plano se perdía entero.
    //
    // Mismo patrón de seam que CompareViewFactory / CancelProgressRequested: el VM decide QUÉ
    // preguntar (puro, testeable) y la View provee la superficie modal. Deliberadamente NO se
    // agrega a IDialogService — esta pregunta necesita una lista de documentos, no un
    // "¿guardar sí/no?" por archivo.

    /// <summary>
    /// Lo enchufa <c>MainWindow.Window_Closing</c> contra el diálogo real. Null en pruebas: el
    /// cierre se CANCELA (ver <see cref="RequestExitCancelled"/>) en vez de descartar en silencio.
    /// </summary>
    internal Func<UnsavedChangesReport, ConfirmResult>? UnsavedChangesPrompt { get; set; }

    /// <summary>
    /// Qué hay sin guardar en TODOS los paneles, más el trabajo largo en vuelo si lo hay.
    ///
    /// La operación en curso sale de la barra de progreso de plugins, que es lo único que el host
    /// ya sabe hoy sobre trabajos en vuelo: dictado en vivo y transcripción de archivo abren su
    /// scope (<c>BeginProgress</c>) y por eso son detectables. Un plugin que trabaje sin abrir
    /// scope es INVISIBLE para el host — acá no se inventa un mecanismo para adivinarlo.
    /// </summary>
    internal UnsavedChangesReport BuildUnsavedReport() =>
        UnsavedChangesReport.Build(
            Groups.Select(g => (IReadOnlyList<OpenTab>)g.OpenTabs).ToList(),
            IsProgressVisible ? ProgressTitle : null);

    /// <summary>Vacía a disco las pestañas sucias de TODOS los paneles. Devuelve los fallos.</summary>
    internal IReadOnlyList<string> SaveAllDirtyTabsBlocking() =>
        Groups.SelectMany(g => g.SaveDirtyTabsBlocking()).ToList();

    /// <summary>
    /// Punto único de cierre. Devuelve <c>true</c> si hay que CANCELAR el cierre.
    /// Cuando devuelve <c>false</c> ya dejó todo persistido: es el llamador el que cierra.
    /// </summary>
    public bool RequestExitCancelled()
    {
        var report = BuildUnsavedReport();

        if (report.ShouldPrompt)
        {
            // Sin diálogo enchufado NO se descarta nada: se frena el cierre. Perder contenido por
            // un cable suelto sería exactamente el bug que este código existe para cerrar, y la
            // salida sigue estando a mano (guardar, o cerrar las pestañas una por una, que sí
            // pregunta) — molesto pero reversible.
            if (UnsavedChangesPrompt is null)
            {
                StatusMessage = "Hay documentos con cambios sin guardar. Guardalos antes de salir.";
                return true;
            }

            switch (UnsavedChangesPrompt(report))
            {
                case ConfirmResult.Cancel:
                    return true;

                case ConfirmResult.Yes:
                    var failed = SaveAllDirtyTabsBlocking();

                    // Falló una escritura, o hay un documento que nunca tuvo ruta en disco (no hay
                    // archivo que actualizar). En los dos casos se ABORTA el cierre: el usuario
                    // pidió guardar y no se guardó todo — cerrar igual sería perder el contenido
                    // justo después de que dijo que no quería perderlo.
                    if (failed.Count > 0)
                    {
                        _dialogService.ShowError(
                            "No se pudieron guardar estos documentos, así que no se cerró la aplicación:\n\n"
                            + string.Join("\n", failed),
                            "Error al guardar");
                        return true;
                    }

                    if (report.HasNeverSaved)
                    {
                        _dialogService.ShowError(
                            report.NeverSavedWarning!,
                            "Faltan documentos por guardar");
                        return true;
                    }
                    break;

                case ConfirmResult.No:
                    // Descarte EXPLÍCITO: el usuario vio los nombres y eligió salir igual.
                    break;
            }
        }

        OnExit();
        return false;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Multi-vault open (Phase 5.1 — replaces the old single-vault switch-and-close-tabs
    /// dance, matching the ctor's startup restore loop above): ADDS <paramref name="path"/>
    /// as a new open root via <see cref="FileService.AddRoot"/>/<see cref="FileTreeViewModel.AddRoot"/>
    /// instead of closing every tab and swapping the single root (decision D3 — opening
    /// another vault must never touch existing tabs).
    /// </summary>
    private void OpenVaultPath(string path)
    {
        _fileService.AddRoot(path);
        FileTree.AddRoot(path);
        _settings.LastVaultPath = path; // kept for the documented rollback path (design "Rollback Plan")
        RegisterKnownVault(path);
        SyncOpenVaultPaths();
        OnPropertyChanged(nameof(VaultName));
        SaveSettings();
    }

    /// <summary>
    /// Closes an open vault root (Phase 5, mirrors <see cref="OpenVaultPath"/>): removes it
    /// from <see cref="FileService"/>/<see cref="FileTree"/> — dropping its explorer section
    /// and disposing its watcher — but leaves its already-open tabs open and editable
    /// (decision D3, spec "Close Toggle Behavior").
    /// </summary>
    private void CloseVaultRoot(string path)
    {
        _fileService.RemoveRoot(path);
        FileTree.RemoveRoot(path);
        SyncOpenVaultPaths();
        OnPropertyChanged(nameof(VaultName));
        SaveSettings();
    }

    /// <summary>Mirrors the live open-root set into the persisted <see cref="AppSettings.OpenVaultPaths"/>
    /// so the next launch restores exactly what's open now (spec "Persisted Open Set").</summary>
    private void SyncOpenVaultPaths() =>
        _settings.OpenVaultPaths = _fileService.VaultRoots.ToList();

    /// <summary>Adds a vault root to the known list (deduplicated, case-insensitive).</summary>
    private void RegisterKnownVault(string path)
    {
        if (!_settings.KnownVaultPaths.Any(
                p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
            _settings.KnownVaultPaths.Add(path);
    }

    /// <summary>
    /// Builds the VM behind the "Administrar vaults" form, wired to the live vault
    /// registry, the real folder picker, and this VM's open-root/close-root/persist
    /// operations (Phase 4 open-set semantics — no single "active" vault anymore).
    /// </summary>
    public VaultsViewModel CreateVaultsViewModel() =>
        new(
            knownPaths: _settings.KnownVaultPaths,
            openPaths:  () => _fileService.VaultRoots,
            pickFolder: PickVaultFolder,
            openRoot:   OpenVaultPath,
            closeRoot:  CloseVaultRoot,
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
