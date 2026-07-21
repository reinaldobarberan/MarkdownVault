using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using MarkdownVault.Models;
using MarkdownVault.ViewModels;

namespace MarkdownVault.Views;

/// <summary>
/// Code-behind for the main window.
/// Responsibilities:
///   1. WebView2 async initialization and virtual-host mapping.
///   2. Pushing preview HTML to the WebView2 whenever it changes.
///   3. Forwarding DataContext font bindings to AvalonEdit.
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel? _vm;
    private bool           _webViewReady;
    private double         _lastExplorerWidth = 240;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded             += async (_, _) => await InitWebViewAsync();
        SourceInitialized  += (_, _) => ApplyTitleBarTheme();
    }

    // ─── DataContext wiring ───────────────────────────────────────────────────

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _vm = DataContext as MainViewModel;
        if (_vm is null) return;

        _vm.Editor.PropertyChanged += Editor_PropertyChanged;
        _vm.PropertyChanged        += Vm_PropertyChanged;

        ApplyViewMode(_vm.Editor.ViewMode);
        ApplyExplorerVisibility(_vm.IsExplorerVisible);

        // Bind font to window so EditorView can inherit via DynamicResource.
        SetBinding(FontFamilyProperty, new System.Windows.Data.Binding(nameof(MainViewModel.FontFamily))
            { Source = _vm });
        SetBinding(FontSizeProperty, new System.Windows.Data.Binding(nameof(MainViewModel.FontSize))
            { Source = _vm });
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm is null) return;

        if (e.PropertyName == nameof(MainViewModel.IsExplorerVisible))
            ApplyExplorerVisibility(_vm.IsExplorerVisible);

        if (e.PropertyName == nameof(MainViewModel.PreviewZoom) && _webViewReady)
            ApplyPreviewZoom(_vm.PreviewZoom);

        if (e.PropertyName == nameof(MainViewModel.IsDarkTheme))
            // Re-theme the native title bar after the resource dictionary swap has run.
            Dispatcher.BeginInvoke(new Action(ApplyTitleBarTheme), DispatcherPriority.Loaded);
    }

    // ─── Native title-bar theming (Windows 11 DWM) ────────────────────────────

    /// <summary>
    /// Colours the non-client area (caption background, title text, border, and the
    /// min/max/close glyphs) to match the active theme, so the title bar follows
    /// Dark/Light like the rest of the window.
    /// </summary>
    private void ApplyTitleBarTheme()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        bool dark     = _vm?.IsDarkTheme ?? false;
        var  caption  = ResourceColor("ToolBarBackground", dark ? Colors.Black : Colors.White);
        var  text     = ResourceColor("Foreground",        dark ? Colors.White : Colors.Black);
        var  border   = ResourceColor("BorderBrush",       caption);

        NativeTitleBar.Apply(hwnd, dark, caption, text, border);
    }

    private Color ResourceColor(string key, Color fallback) =>
        (TryFindResource(key) as SolidColorBrush)?.Color ?? fallback;

    private static class NativeTitleBar
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20; // dark/light glyphs
        private const int DWMWA_BORDER_COLOR            = 34; // Windows 11+
        private const int DWMWA_CAPTION_COLOR           = 35; // Windows 11+
        private const int DWMWA_TEXT_COLOR              = 36; // Windows 11+

        public static void Apply(IntPtr hwnd, bool dark, Color caption, Color text, Color border)
        {
            int darkFlag = dark ? 1 : 0;
            TrySet(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, darkFlag);
            // Caption/text/border colours require Windows 11 (build 22000+); ignored otherwise.
            TrySet(hwnd, DWMWA_CAPTION_COLOR, ToColorRef(caption));
            TrySet(hwnd, DWMWA_TEXT_COLOR,    ToColorRef(text));
            TrySet(hwnd, DWMWA_BORDER_COLOR,  ToColorRef(border));
        }

        private static void TrySet(IntPtr hwnd, int attribute, int value)
        {
            try { DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)); }
            catch { /* attribute unsupported on this OS build — degrade gracefully */ }
        }

        // DWM expects a COLORREF: 0x00BBGGRR.
        private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);
    }

    private void Editor_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.PreviewHtml) && _webViewReady)
            PushPreview();

        if (e.PropertyName == nameof(EditorViewModel.ViewMode) && _vm is not null && !_vm.Editor.ShowGraph)
            ApplyViewMode(_vm.Editor.ViewMode);

        if (e.PropertyName == nameof(EditorViewModel.ShowGraph) && _vm is not null)
            ApplyGraphMode(_vm.Editor.ShowGraph);
    }

    /// <summary>
    /// When the graph is active it takes over the whole content area to the right of
    /// the explorer, so the preview column collapses. Turning it off restores the
    /// current view mode.
    /// </summary>
    private void ApplyGraphMode(bool graph)
    {
        if (_vm is null) return;

        if (graph)
        {
            EditorColumn.Width    = new GridLength(1, GridUnitType.Star);
            EditorColumn.MinWidth = 200;
            MidSplitterColumn.Width = new GridLength(0);
            PreviewColumn.Width     = new GridLength(0);
            PreviewColumn.MinWidth  = 0;
        }
        else
        {
            ApplyViewMode(_vm.Editor.ViewMode);
        }
    }

    private void ApplyViewMode(ViewMode mode)
    {
        // Editor column (col 2)
        EditorColumn.Width    = mode == ViewMode.ViewerOnly
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);
        EditorColumn.MinWidth = mode == ViewMode.ViewerOnly ? 0 : 200;

        // Mid splitter column (col 3)
        MidSplitterColumn.Width = mode == ViewMode.EditAndPreview
            ? new GridLength(4)
            : new GridLength(0);

        // Preview column (col 4)
        PreviewColumn.Width    = mode == ViewMode.EditorOnly
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);
        PreviewColumn.MinWidth = mode == ViewMode.EditorOnly ? 0 : 200;
    }

    // ─── Explorer toggle ─────────────────────────────────────────────────────

    private void ApplyExplorerVisibility(bool visible)
    {
        if (visible)
        {
            ExplorerColumn.Width    = new GridLength(_lastExplorerWidth);
            ExplorerColumn.MinWidth = 150;
            ExplorerColumn.MaxWidth = 500;
            ExplorerSplitterColumn.Width = new GridLength(4);
            FileTreePanel.Visibility    = Visibility.Visible;
            ExplorerSplitter.Visibility = Visibility.Visible;
        }
        else
        {
            // Save current width before collapsing.
            if (ExplorerColumn.Width.Value > 0)
                _lastExplorerWidth = ExplorerColumn.Width.Value;

            ExplorerColumn.Width    = new GridLength(0);
            ExplorerColumn.MinWidth = 0;
            ExplorerColumn.MaxWidth = 0;
            ExplorerSplitterColumn.Width = new GridLength(0);
            FileTreePanel.Visibility    = Visibility.Collapsed;
            ExplorerSplitter.Visibility = Visibility.Collapsed;
        }
    }

    // ─── WebView2 ────────────────────────────────────────────────────────────

    private async Task InitWebViewAsync()
    {
        try
        {
            await PreviewWebView.EnsureCoreWebView2Async();

            // Map the virtual host "vault.local" → vault root so relative image
            // paths in generated HTML resolve correctly without temp files.
            PreviewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "vault.local",
                System.IO.Path.GetTempPath(),   // placeholder; updated when vault opens
                CoreWebView2HostResourceAccessKind.Allow);

            _webViewReady = true;

            // ── Intercept link clicks ──
            PreviewWebView.CoreWebView2.NavigationStarting += async (_, args) =>
            {
                // Allow NavigateToString() — uses the about:blank scheme.
                if (args.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                    return;

                // Internal links via vault.local base URL.
                if (args.Uri.StartsWith("http://vault.local/", StringComparison.OrdinalIgnoreCase))
                {
                    args.Cancel = true;
                    var relativePath = Uri.UnescapeDataString(
                        args.Uri["http://vault.local/".Length..]);

                    if (_vm?.Editor.ActiveTab is null || string.IsNullOrEmpty(relativePath))
                        return;

                    // Ignore image/asset links — let them load normally.
                    var ext = System.IO.Path.GetExtension(relativePath).ToLowerInvariant();
                    var imageExts = new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg" };
                    if (imageExts.Contains(ext)) return;

                    try
                    {
                        var resolved = App.FileService!.ResolveInternalLink(
                            relativePath, _vm.Editor.ActiveTab.FilePath);
                        await _vm.Editor.NavigateToLinkAsync(resolved);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Link resolution failed: {ex.Message}");
                    }
                    return;
                }

                // External links → open in system browser.
                if (args.Uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    args.Cancel = true;
                    try
                    {
                        System.Diagnostics.Process.Start(
                            new System.Diagnostics.ProcessStartInfo(args.Uri)
                                { UseShellExecute = true });
                    }
                    catch { /* ignore if browser can't be launched */ }
                }
            };

            // Apply persisted zoom level.
            if (_vm is not null)
                ApplyPreviewZoom(_vm.PreviewZoom);

            if (_vm is not null)
                _vm.Editor.PropertyChanged += (_, e2) =>
                {
                    if (e2.PropertyName == nameof(EditorViewModel.PreviewHtml))
                        PushPreview();
                };

            PushPreview();
        }
        catch (Exception ex)
        {
            // WebView2 runtime might not be installed.
            System.Diagnostics.Debug.WriteLine($"WebView2 init failed: {ex.Message}");
        }
    }

    private void PushPreview()
    {
        if (!_webViewReady || _vm is null) return;

        // Re-map virtual host to current vault root so images load correctly.
        var vaultRoot = App.FileService?.VaultRoot ?? System.IO.Path.GetTempPath();
        if (System.IO.Directory.Exists(vaultRoot))
        {
            try
            {
                PreviewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "vault.local", vaultRoot,
                    CoreWebView2HostResourceAccessKind.Allow);
            }
            catch { /* ignore if already mapped identically */ }
        }

        var html = _vm.Editor.PreviewHtml;
        if (!string.IsNullOrEmpty(html))
            PreviewWebView.NavigateToString(html);
    }

    private void ApplyPreviewZoom(double zoom)
    {
        if (!_webViewReady) return;
        try
        {
            PreviewWebView.ZoomFactor = zoom;
        }
        catch { /* ignore if WebView2 not fully initialized */ }
    }

    // ─── Window events ───────────────────────────────────────────────────────

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        _vm?.OnExit();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // ─── Export to PNG ───────────────────────────────────────────────────────

    /// <summary>
    /// Captures the full rendered page (not just the viewport) from WebView2
    /// and saves it as a PNG file using the Chrome DevTools Protocol.
    /// </summary>
    private async void ExportToPng_Click(object sender, RoutedEventArgs e)
    {
        if (!_webViewReady || _vm is null)
        {
            MessageBox.Show(
                "No hay contenido para exportar. Abrí un archivo primero.",
                "Sin vista previa", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Default filename based on the active tab.
        var defaultName = "export";
        if (_vm.Editor.ActiveTab is not null)
            defaultName = System.IO.Path.GetFileNameWithoutExtension(_vm.Editor.ActiveTab.FilePath);

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "Exportar vista previa como imagen",
            Filter     = "PNG Image|*.png",
            DefaultExt = ".png",
            FileName   = defaultName
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            // 1. Get full page dimensions via JavaScript.
            var dimScript = "JSON.stringify({w: document.documentElement.scrollWidth, h: document.documentElement.scrollHeight})";
            var dimResult = await PreviewWebView.CoreWebView2.ExecuteScriptAsync(dimScript);

            // ExecuteScriptAsync returns a JSON-encoded string (e.g. "\"{...}\"").
            var dimJson = System.Text.Json.JsonSerializer.Deserialize<string>(dimResult);
            using var dimDoc = System.Text.Json.JsonDocument.Parse(dimJson!);
            var pageWidth  = dimDoc.RootElement.GetProperty("w").GetInt32();
            var pageHeight = dimDoc.RootElement.GetProperty("h").GetInt32();

            // 2. Capture the full page via Chrome DevTools Protocol.
            var captureParams = System.Text.Json.JsonSerializer.Serialize(new
            {
                format = "png",
                clip = new { x = 0, y = 0, width = pageWidth, height = pageHeight, scale = 2 },
                captureBeyondViewport = true
            });

            var captureResult = await PreviewWebView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                "Page.captureScreenshot", captureParams);

            using var captureDoc = System.Text.Json.JsonDocument.Parse(captureResult);
            var base64Data = captureDoc.RootElement.GetProperty("data").GetString();

            if (string.IsNullOrEmpty(base64Data))
            {
                MessageBox.Show("No se pudo capturar la imagen.", "Error de exportación",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 3. Decode and save.
            var imageBytes = Convert.FromBase64String(base64Data);
            await System.IO.File.WriteAllBytesAsync(dlg.FileName, imageBytes);

            MessageBox.Show(
                $"Imagen exportada exitosamente:\n{dlg.FileName}",
                "Exportación completada", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error al exportar la imagen:\n{ex.Message}",
                "Error de exportación", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ─── Global tab bar event handlers ────────────────────────────────────────

    /// <summary>Left-click on a tab → switch to it.</summary>
    private void Tab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm is null) return;
        if (sender is FrameworkElement fe && fe.DataContext is OpenTab tab)
        {
            _vm.Editor.SwitchToTabCommand.Execute(tab);
            e.Handled = true;
        }
    }

    /// <summary>Middle-click on a tab → close it.</summary>
    private void Tab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm is null) return;
        if (e.ChangedButton == MouseButton.Middle &&
            sender is FrameworkElement fe && fe.DataContext is OpenTab tab)
        {
            _vm.Editor.CloseTabCommand.Execute(tab);
            e.Handled = true;
        }
    }
}
