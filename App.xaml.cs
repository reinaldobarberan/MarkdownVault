using System.Windows;
using System.Windows.Threading;
using MarkdownVault.Services;
using MarkdownVault.Services.Plugins;
using MarkdownVault.ViewModels;
using MarkdownVault.Views;

namespace MarkdownVault;

public partial class App : Application
{
    // Expose FileService so MainWindow.xaml.cs can reach VaultRoot for WebView2 mapping.
    public static FileService     FileService     { get; private set; } = null!;
    public static MarkdownService MarkdownService { get; private set; } = null!;
    public static SettingsService SettingsService { get; private set; } = null!;
    // Spell checker uses the OS dictionaries for the current UI culture; the editor
    // reads it to draw red squiggles. Falls back to disabled if the API/language is missing.
    public static ISpellCheckService SpellCheckService { get; private set; } = null!;

    // Plugin subsystem: registry aggregates contributions; manager discovers/loads
    // plugins from the Plugins/ folder next to the executable.
    public static PluginRegistry PluginRegistry { get; private set; } = null!;
    public static PluginManager  PluginManager  { get; private set; } = null!;

    /// <summary>
    /// Multiplexor de la barra de progreso de plugins (SDK 1.3.0) y sumidero del
    /// log de plugins a archivo. Se exponen para diagnóstico (la ruta del log) y
    /// porque el ciclo de vida es el de la app.
    /// </summary>
    public static PluginProgressCoordinator PluginProgress { get; private set; } = null!;
    public static FilePluginLogSink         PluginLog      { get; private set; } = null!;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        FileService       = new FileService();
        SettingsService   = new SettingsService();

        // Registry must exist before MarkdownService (which reads plugin assets).
        PluginRegistry    = new PluginRegistry();
        MarkdownService   = new MarkdownService(PluginRegistry);

        SpellCheckService = new WindowsSpellCheckService(SettingsService.Load().SpellCheckLanguage);

        var dialogService = new WpfDialogService();

        // Build the VM first so the theme is applied before the splash reads its brushes.
        var mainVm = new MainViewModel(FileService, MarkdownService, SettingsService, PluginRegistry, dialogService);

        // ── Canal de progreso (SDK 1.3.0) ────────────────────────────────────
        // El coordinador es el ÚNICO que sabe de hilos en este asunto: acá se le
        // inyecta el marshaling al hilo de UI, igual que se hace con OpenFileAction.
        // Un plugin reporta desde donde quiera y nunca toca un Dispatcher.
        // BeginInvoke (no Invoke): reportar progreso jamás debe frenar al hilo que
        // está haciendo el trabajo largo.
        PluginProgress = new PluginProgressCoordinator(
            action => Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, action));

        // Snapshot → propiedades primitivas del VM. La proyección se hace acá para
        // que MainViewModel no dependa de ningún tipo del subsistema de plugins.
        PluginProgress.ActiveChanged += snapshot =>
        {
            if (snapshot is null) mainVm.HideProgress();
            else mainVm.ShowProgress(
                snapshot.Title, snapshot.Message, snapshot.Percent,
                snapshot.OtherCount, snapshot.IsCancelling);
        };

        // El botón «Cancelar» de la barra, de vuelta hacia el coordinador (dos
        // tiempos: primero cancela el token, la segunda vez descarta el scope).
        mainVm.CancelProgressRequested = PluginProgress.CancelOrDiscardActive;

        // Log de plugins a %AppData%/MarkdownVault/logs/plugins.log. Se enchufa acá
        // y no por default en PluginManager a propósito: las pruebas construyen el
        // manager sin sumidero y no ensucian el disco del usuario.
        PluginLog = new FilePluginLogSink();

        // Wire the read-only host facade, then discover and load plugins from Plugins/.
        // Loading after the VM exists is safe: no preview renders until a file opens.
        var hostServices = new HostServices(FileService, PluginProgress)
        {
            DarkThemeProvider  = () => mainVm.IsDarkTheme,
            // Resolved against FocusedGroup at call time (this lambda captures mainVm, not a
            // group instance) so ActiveFilePath always answers for whichever pane has focus.
            ActiveFileProvider = () => mainVm.FocusedGroup.ActiveTab?.FilePath,
            // StatusMessage promoted to MainViewModel in Phase 2 — the Editor facade only
            // covers members still living on the group, so this no longer goes through it.
            StatusSink         = msg => mainVm.StatusMessage = msg,
            // Abre una ruta absoluta ya resuelta/confinada por HostServices.OpenVaultFile.
            // Marshaling a UI explícito: los plugins pueden invocar el host fuera del hilo
            // de UI, y OpenFileAsync toca ObservableCollection<OpenTab> (solo UI thread).
            // Routed through the workbench's path-uniqueness invariant (Phase 3, HARD GATE) —
            // a plugin open of an already-open file must focus the owning group, not duplicate it.
            OpenFileAction     = path => Current.Dispatcher.Invoke(() => _ = mainVm.OpenInFocusedGroupAsync(path))
        };
        PluginManager = new PluginManager(
            PluginRegistry, hostServices, SettingsService,
            progress: PluginProgress, logSink: PluginLog);
        PluginManager.LoadAll();

        // When the active plugin set changes (enable/disable from the Plugins window),
        // re-render the current preview so the change is visible immediately. The plugin
        // shell changed for every pane, not just the focused one, so fan out to all groups.
        PluginRegistry.Changed += () =>
            Current.Dispatcher.Invoke(mainVm.RefreshPreviewFromPluginsAllGroups);

        var splash = new Views.SplashWindow();
        splash.Show();

        var window = new Views.MainWindow { DataContext = mainVm };
        MainWindow = window;   // so dialogs (InputDialog, LinkPicker) get the right owner

        // "Comparar archivos": el VM computa/renderiza el diff y orienta el merge (testeable);
        // acá se provee la superficie real —una Window WebView2 bidireccional— con el main
        // window como Owner. Fábrica: el VM crea/cierra la vista según la sesión.
        mainVm.CompareViewFactory = () => new Views.DiffWindow { Owner = window };

        // Keep the splash up for a short minimum, then reveal the main window.
        // Showing the main window before closing the splash keeps a window alive at all
        // times, so OnLastWindowClose shutdown never fires between the two.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            window.Show();
            splash.Close();
        };
        timer.Start();
    }
}
