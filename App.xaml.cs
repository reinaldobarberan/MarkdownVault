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

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        FileService       = new FileService();
        SettingsService   = new SettingsService();

        // Registry must exist before MarkdownService (which reads plugin assets).
        PluginRegistry    = new PluginRegistry();
        MarkdownService   = new MarkdownService(PluginRegistry);

        SpellCheckService = new WindowsSpellCheckService(SettingsService.Load().SpellCheckLanguage);

        // Build the VM first so the theme is applied before the splash reads its brushes.
        var mainVm = new MainViewModel(FileService, MarkdownService, SettingsService, PluginRegistry);

        // Wire the read-only host facade, then discover and load plugins from Plugins/.
        // Loading after the VM exists is safe: no preview renders until a file opens.
        var hostServices = new HostServices(FileService)
        {
            DarkThemeProvider  = () => mainVm.IsDarkTheme,
            ActiveFileProvider = () => mainVm.Editor.ActiveTab?.FilePath,
            StatusSink         = msg => mainVm.Editor.StatusMessage = msg,
            // Abre una ruta absoluta ya resuelta/confinada por HostServices.OpenVaultFile.
            // Marshaling a UI explícito: los plugins pueden invocar el host fuera del hilo
            // de UI, y OpenFileAsync toca ObservableCollection<OpenTab> (solo UI thread).
            OpenFileAction     = path => Current.Dispatcher.Invoke(() => _ = mainVm.Editor.OpenFileAsync(path))
        };
        PluginManager = new PluginManager(PluginRegistry, hostServices, SettingsService);
        PluginManager.LoadAll();

        // When the active plugin set changes (enable/disable from the Plugins window),
        // re-render the current preview so the change is visible immediately.
        PluginRegistry.Changed += () =>
            Current.Dispatcher.Invoke(() => mainVm.Editor.RefreshPreviewFromPlugins());

        var splash = new Views.SplashWindow();
        splash.Show();

        var window = new Views.MainWindow { DataContext = mainVm };
        MainWindow = window;   // so dialogs (InputDialog, LinkPicker) get the right owner

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
