using System.Windows;
using System.Windows.Threading;
using MarkdownVault.Services;
using MarkdownVault.ViewModels;
using MarkdownVault.Views;

namespace MarkdownVault;

public partial class App : Application
{
    // Expose FileService so MainWindow.xaml.cs can reach VaultRoot for WebView2 mapping.
    public static FileService     FileService     { get; private set; } = null!;
    public static MarkdownService MarkdownService { get; private set; } = null!;
    public static SettingsService SettingsService { get; private set; } = null!;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        FileService     = new FileService();
        MarkdownService = new MarkdownService();
        SettingsService = new SettingsService();

        // Build the VM first so the theme is applied before the splash reads its brushes.
        var mainVm = new MainViewModel(FileService, MarkdownService, SettingsService);

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
