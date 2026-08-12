using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using MarkdownVault.Services;
using MarkdownVault.ViewModels;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MarkdownVault.Views;

/// <summary>
/// Ventana de comparación lado-a-lado sobre WebView2. Además de mostrar el diff, es
/// bidireccional: cada botón ◀/▶ del HTML hace <c>postMessage</c> y esta ventana lo
/// traduce a un evento <see cref="MergeRequested"/> que el <see cref="MainViewModel"/>
/// aplica al buffer del panel correspondiente. Deliberadamente delgada: no computa diff
/// ni edita texto; solo transporta HTML hacia el WebView y mensajes hacia el host.
/// </summary>
public sealed class DiffWindow : Window, ICompareView
{
    private readonly WebView2 _web = new();
    private bool    _ready;
    private string? _pendingHtml;

    public event Action<CompareMergeRequest>? MergeRequested;
    public event Action?                       ViewClosed;

    public DiffWindow()
    {
        Width  = 1100;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = _web;

        Closed += (_, _) => ViewClosed?.Invoke();
        Loaded += async (_, _) => await InitAsync();
    }

    public void Show(string html, bool isDark, string title)
    {
        Title = title;
        ApplyBaseColor(isDark);
        _pendingHtml = html;
        if (_ready && _web.CoreWebView2 is not null) _web.NavigateToString(html);
        base.Show();
        Activate();
    }

    public void Reload(string html)
    {
        _pendingHtml = html;
        if (_ready && _web.CoreWebView2 is not null) _web.NavigateToString(html);
    }

    /// <summary>Color base pintado antes/entre navegaciones para evitar el flash blanco (mismo truco que el preview).</summary>
    private void ApplyBaseColor(bool isDark)
    {
        var baseColor = isDark
            ? (Color)ColorConverter.ConvertFromString("#1e1e1e")
            : Colors.White;
        Background = new SolidColorBrush(baseColor);
        _web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(
            baseColor.A, baseColor.R, baseColor.G, baseColor.B);
    }

    private async Task InitAsync()
    {
        if (_ready) return;
        try
        {
            await _web.EnsureCoreWebView2Async();
            _web.CoreWebView2.WebMessageReceived += OnWebMessage;
            _ready = true;
            if (_pendingHtml is not null) _web.NavigateToString(_pendingHtml);
        }
        catch (Exception ex)
        {
            // Mismo criterio que el preview del host: si el runtime de WebView2 no está
            // instalado, se degrada sin romper la app.
            System.Diagnostics.Debug.WriteLine($"DiffWindow WebView2 init failed: {ex.Message}");
            MessageBox.Show(
                "No se pudo inicializar la vista de comparación (WebView2).",
                "Comparar archivos", MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
        }
    }

    /// <summary>Traduce el <c>postMessage</c> de un botón ◀/▶ a un pedido de merge tipado.</summary>
    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(json)) return;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            int row   = root.GetProperty("row").GetInt32();
            var dir   = root.GetProperty("dir").GetString();
            bool block = root.TryGetProperty("block", out var b) && b.GetBoolean();

            var direction = dir == "right" ? MergeDirection.ToRight : MergeDirection.ToLeft;
            MergeRequested?.Invoke(new CompareMergeRequest(row, direction, block));
        }
        catch
        {
            // Mensaje malformado o inesperado: ignorar en vez de romper la sesión.
        }
    }
}
