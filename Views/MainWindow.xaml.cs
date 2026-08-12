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

    // Preview subscription lifecycle (design §5.2, bug #272 fix — pulled forward from Phase 5
    // because Phase 4's SE-8 "preview follows focus" cannot work correctly without it): exactly
    // one field can hold a subscription, unsubscribe always precedes assignment, the handler is
    // a named method (hence removable), and ReferenceEquals makes repeat calls free.
    private EditorGroupViewModel? _previewSource;

    // Preview state, used to decide between an in-place DOM patch (same page → keeps
    // scroll, no flash) and a full NavigateToString (different file/theme/plugin set).
    private string?        _lastPreviewPath;
    private bool           _lastPreviewDark;
    private int            _lastPreviewShellVersion = -1;
    private bool           _previewLoaded;     // last navigation finished → __mvSetBody is available

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

        BindPreviewSource(_vm.FocusedGroup);
        _vm.PropertyChanged        += Vm_PropertyChanged;

        ApplyViewMode(_vm.ViewMode);
        ApplyExplorerVisibility(_vm.IsExplorerVisible);
        ApplySplit(_vm.IsSplit);

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
        {
            // Keep the WebView2 base colour in sync so the next navigation doesn't flash.
            if (_webViewReady) ApplyWebViewBackground();
            // Re-theme the native title bar after the resource dictionary swap has run.
            Dispatcher.BeginInvoke(new Action(ApplyTitleBarTheme), DispatcherPriority.Loaded);
        }

        // ViewMode/ShowGraph were promoted from EditorGroupViewModel to MainViewModel in
        // Phase 2 — the facade (_vm.Editor) can't cover promoted members, so these are now
        // read off _vm directly instead of via the old Editor_PropertyChanged handler.
        if (e.PropertyName == nameof(MainViewModel.ViewMode) && !_vm.ShowGraph)
            ApplyViewMode(_vm.ViewMode);

        if (e.PropertyName == nameof(MainViewModel.ShowGraph))
            ApplyGraphMode(_vm.ShowGraph);

        // Phase 4: split geometry (pane B width/visibility, splitter).
        if (e.PropertyName == nameof(MainViewModel.IsSplit))
            ApplySplit(_vm.IsSplit);

        // Phase 4 (pulled forward from Phase 5, design §5.2): re-point the single preview
        // subscription at the newly-focused group — unsubscribe old, subscribe new, push its
        // content immediately. This is what makes SE-8 "preview follows focus" actually work.
        if (e.PropertyName == nameof(MainViewModel.FocusedGroup))
            BindPreviewSource(_vm.FocusedGroup);
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

    /// <summary>
    /// Owner of the single preview subscription (design §5.2, bug #272 fix). Unsubscribe
    /// ALWAYS precedes assignment, the handler is a named method (removable), and the
    /// ReferenceEquals guard makes repeat calls with the same group free. Called from
    /// <see cref="OnDataContextChanged"/> (initial bind) and <see cref="Vm_PropertyChanged"/>
    /// whenever <see cref="MainViewModel.FocusedGroup"/> changes.
    /// </summary>
    private void BindPreviewSource(EditorGroupViewModel? g)
    {
        if (ReferenceEquals(_previewSource, g)) return;
        if (_previewSource is not null)
            _previewSource.PropertyChanged -= PreviewSource_PropertyChanged;
        _previewSource = g;
        if (_previewSource is not null)
            _previewSource.PropertyChanged += PreviewSource_PropertyChanged;
        PushPreview();
    }

    private void PreviewSource_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorGroupViewModel.PreviewHtml) && _webViewReady)
            PushPreview();
    }

    /// <summary>
    /// Task 4.11: nested pane grid — pane A / splitter / pane B. Widths are imperative
    /// (GridLength star-collapse has no clean declarative form, matching
    /// <see cref="ApplyExplorerVisibility"/>'s precedent); the elements' own Visibility is
    /// bound declaratively in XAML to <see cref="MainViewModel.IsSplit"/>.
    /// </summary>
    private void ApplySplit(bool split)
    {
        if (_vm is null) return;

        if (split)
        {
            var ratio = Math.Clamp(_vm.SplitRatio, 0.1, 0.9);
            PaneAColumn.Width    = new GridLength(ratio, GridUnitType.Star);
            PaneBColumn.Width    = new GridLength(1 - ratio, GridUnitType.Star);
            PaneBColumn.MinWidth = 150;
            PaneSplitterColumn.Width = new GridLength(4);
        }
        else
        {
            PaneAColumn.Width    = new GridLength(1, GridUnitType.Star);
            PaneBColumn.Width    = new GridLength(0);
            PaneBColumn.MinWidth = 0;
            PaneSplitterColumn.Width = new GridLength(0);
        }
    }

    /// <summary>Task 4.11: captures the splitter's dropped position as a ratio (0..1) so it
    /// survives a restart (SE-3) — same capture-before-collapse trick as <c>_lastExplorerWidth</c>.</summary>
    private void PaneSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (_vm is null) return;
        var total = PaneAColumn.ActualWidth + PaneBColumn.ActualWidth;
        if (total > 0)
            _vm.SplitRatio = PaneAColumn.ActualWidth / total;
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
            ApplyViewMode(_vm.ViewMode);
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

    /// <summary>
    /// Sets the WebView2 base colour (painted between navigations and before the
    /// document renders) to match the preview HTML background for the active theme.
    /// Without this, every file switch flashes the default white during the brief
    /// window where NavigateToString has torn down the old page but not painted the
    /// new one. Dark: #0D1117 (GitHub dark body), Light: #FFFFFF.
    /// </summary>
    private void ApplyWebViewBackground()
    {
        bool dark = _vm?.IsDarkTheme ?? false;
        PreviewWebView.DefaultBackgroundColor = dark
            ? System.Drawing.Color.FromArgb(0x0D, 0x11, 0x17)
            : System.Drawing.Color.White;
    }

    private async Task InitWebViewAsync()
    {
        try
        {
            // Set before EnsureCoreWebView2Async so the very first paint is themed.
            ApplyWebViewBackground();

            await PreviewWebView.EnsureCoreWebView2Async();

            // Map the virtual host "vault.local" → vault root so relative image
            // paths in generated HTML resolve correctly without temp files.
            PreviewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "vault.local",
                System.IO.Path.GetTempPath(),   // placeholder; updated when vault opens
                CoreWebView2HostResourceAccessKind.Allow);

            _webViewReady = true;

            // Track load completion so we only DOM-patch a page that finished loading
            // (its __mvSetBody helper is defined); otherwise fall back to full navigation.
            PreviewWebView.CoreWebView2.NavigationCompleted += (_, _) => _previewLoaded = true;

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

                    if (_vm?.FocusedGroup.ActiveTab is null || string.IsNullOrEmpty(relativePath))
                        return;

                    // Ignore image/asset links — let them load normally.
                    var ext = System.IO.Path.GetExtension(relativePath).ToLowerInvariant();
                    var imageExts = new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg" };
                    if (imageExts.Contains(ext)) return;

                    try
                    {
                        var resolved = App.FileService!.ResolveInternalLink(
                            relativePath, _vm.FocusedGroup.ActiveTab.FilePath);
                        await _vm.FocusedGroup.NavigateToLinkAsync(resolved);
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

            // NOTE: the preview subscription itself is owned by BindPreviewSource (bug #272 —
            // this used to be a SECOND, anonymous-lambda subscription here with no stored
            // reference, impossible to unsubscribe, causing every preview update to push
            // twice). BindPreviewSource was already wired in OnDataContextChanged before the
            // WebView2 finished initializing; just push once now that it's ready.
            PushPreview();
        }
        catch (Exception ex)
        {
            // WebView2 runtime might not be installed.
            System.Diagnostics.Debug.WriteLine($"WebView2 init failed: {ex.Message}");
        }
    }

    private async void PushPreview()
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

        // Preview always tracks _previewSource (BindPreviewSource, kept in sync with
        // FocusedGroup) — never the Editor facade, and never a fixed pane (SE-8).
        var html = _previewSource?.PreviewHtml ?? string.Empty;

        // Empty means no preview source, or no active tab in it (e.g. the last tab was
        // closed, or pane B is freshly split and still empty per SE-1). We must still
        // navigate — skipping would leave the previous file's HTML on screen. Push a
        // minimal blank page whose background matches the theme so it clears cleanly.
        if (string.IsNullOrEmpty(html))
        {
            bool darkBlank = _vm.IsDarkTheme;
            var bg = darkBlank ? "#0D1117" : "#FFFFFF";
            _previewLoaded = false;
            PreviewWebView.NavigateToString(
                $"<!DOCTYPE html><html><body style=\"margin:0;background:{bg};\"></body></html>");
            _lastPreviewPath = null;
            return;
        }

        var currentPath  = _previewSource?.ActiveTab?.FilePath;
        bool dark         = _vm.IsDarkTheme;
        int  shellVersion = _previewSource?.PreviewShellVersion ?? -1;
        var  bodyHtml     = _previewSource?.PreviewBodyHtml ?? string.Empty;

        // In-place DOM patch when nothing but the content changed: same file, same theme,
        // same plugin set, and the page has finished loading (so __mvSetBody exists). This
        // avoids a reload entirely — no flash, and scroll is preserved intrinsically. Raw
        // .html files have no body fragment and always take the full-navigation path.
        bool canPatch = _previewLoaded
            && !string.IsNullOrEmpty(bodyHtml)
            && currentPath is not null
            && string.Equals(currentPath, _lastPreviewPath, StringComparison.OrdinalIgnoreCase)
            && dark == _lastPreviewDark
            && shellVersion == _lastPreviewShellVersion;

        if (canPatch)
        {
            try
            {
                var js = System.Text.Json.JsonSerializer.Serialize(bodyHtml);
                await PreviewWebView.CoreWebView2.ExecuteScriptAsync($"window.__mvSetBody({js});");
                return;
            }
            catch { /* fall through to a full navigation */ }
        }

        // Full navigation (different file/theme/plugins, first load, or patch failed).
        _previewLoaded = false;
        PreviewWebView.NavigateToString(html);
        _lastPreviewPath         = currentPath;
        _lastPreviewDark         = dark;
        _lastPreviewShellVersion = shellVersion;
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

    /// <summary>Opens the vault manager form (list / add / switch / remove roots).</summary>
    private void ManageVaults_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var window = new VaultsWindow
        {
            Owner       = this,
            DataContext = _vm.CreateVaultsViewModel()
        };
        window.ShowDialog();
    }

    /// <summary>Opens the plugins manager window (list / enable / disable).</summary>
    private void Plugins_Click(object sender, RoutedEventArgs e)
    {
        var window = new PluginsWindow
        {
            Owner       = this,
            DataContext = new PluginsViewModel(App.PluginManager)
        };
        window.ShowDialog();
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
        var defaultName = Services.ExportNaming.DefaultFileName(_vm.FocusedGroup.ActiveTab?.FilePath);

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

    // ─── Export to PDF ───────────────────────────────────────────────────────

    /// <summary>
    /// Prints the rendered preview to a PDF using WebView2's native
    /// <see cref="CoreWebView2.PrintToPdfAsync"/> — same Chromium engine that paints the
    /// on-screen preview, so the PDF matches the preview exactly (no second renderer).
    /// Backgrounds are forced on so dark-theme pages stay readable (light text would be
    /// invisible on the printer's default white page otherwise).
    /// </summary>
    private async void ExportToPdf_Click(object sender, RoutedEventArgs e)
    {
        if (!_webViewReady || _vm is null)
        {
            MessageBox.Show(
                "No hay contenido para exportar. Abrí un archivo primero.",
                "Sin vista previa", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Default filename based on the active tab (mirrors the PNG export).
        var defaultName = Services.ExportNaming.DefaultFileName(_vm.FocusedGroup.ActiveTab?.FilePath);

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "Exportar vista previa como PDF",
            Filter     = "PDF Document|*.pdf",
            DefaultExt = ".pdf",
            FileName   = defaultName
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            // WYSIWYG: keep backgrounds so the exported PDF matches the preview (and dark
            // theme stays legible). Other settings keep WebView2's sensible defaults.
            var settings = PreviewWebView.CoreWebView2.Environment.CreatePrintSettings();
            settings.ShouldPrintBackgrounds = true;

            var ok = await PreviewWebView.CoreWebView2.PrintToPdfAsync(dlg.FileName, settings);

            if (ok)
            {
                MessageBox.Show(
                    $"PDF exportado exitosamente:\n{dlg.FileName}",
                    "Exportación completada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    "No se pudo generar el PDF.", "Error de exportación",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error al exportar el PDF:\n{ex.Message}",
                "Error de exportación", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
