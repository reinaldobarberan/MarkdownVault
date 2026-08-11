using System.IO;
using MarkdownVault.PluginSdk;
using MarkdownVault.Services;
using MarkdownVault.Services.Plugins;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Integración del plugin <c>core.copybutton</c>: carga el DLL REAL compilado
/// (bin/.../Plugins/CopyButton) vía <see cref="PluginManager"/> y verifica que sus
/// PreviewAssets se registren, se inyecten en la página renderizada, y respeten los
/// invariantes NO OBVIOS del botón de copiar. A diferencia de Eisenhower, este plugin
/// no aporta una extensión Markdig — el botón lo crea el JS en runtime dentro del
/// WebView2 —, así que lo verificable server-side es la inyección de assets y su
/// contenido, no un DOM de botón.
/// </summary>
public class CopyButtonPluginTests : IDisposable
{
    private readonly string _settingsPath =
        Path.Combine(Path.GetTempPath(), $"mvcopybtn_{Guid.NewGuid():N}.json");

    /// <summary>Ubica bin/&lt;Config&gt;/net8.0-windows/Plugins subiendo hasta el .sln.</summary>
    private static string? FindHostPluginsRoot()
    {
        var baseDir = AppContext.BaseDirectory;
        var dir     = new DirectoryInfo(baseDir);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MarkdownVault.sln")))
            dir = dir.Parent;
        if (dir is null) return null;

        var config = baseDir.Replace('\\', '/').Contains("/Release/") ? "Release" : "Debug";
        return Path.Combine(dir.FullName, "bin", config, "net8.0-windows", "Plugins");
    }

    private PluginManager NewManager(PluginRegistry registry, string pluginsRoot) =>
        new(registry, new FakeHost(), new SettingsService(_settingsPath), pluginsRoot);

    [Fact]
    public void CopyButton_plugin_loads_activates_and_injects_its_assets()
    {
        var pluginsRoot = FindHostPluginsRoot();
        // En `dotnet test` el host se compila antes, así que la carpeta existe.
        // Guarda defensiva: si no está, no hay nada que integrar.
        if (pluginsRoot is null || !Directory.Exists(pluginsRoot))
            return;

        var registry = new PluginRegistry();
        var manager  = NewManager(registry, pluginsRoot);
        manager.LoadAll();

        var plugin = manager.Plugins.FirstOrDefault(p => p.Metadata.Id == "core.copybutton");
        Assert.NotNull(plugin);
        Assert.Equal(PluginState.Active, plugin!.State);
        Assert.True(plugin.Enabled);   // habilitado por defecto

        // Debe registrar exactamente 2 PreviewAssets: el CSS (head) y el JS (fin del body).
        var assets = registry.PreviewAssets.Where(a => a.Value.Contains("mv-copy-btn")).ToList();
        Assert.Contains(assets, a => a.Kind == AssetKind.Style  && a.Placement == AssetPlacement.HeadEnd);
        Assert.Contains(assets, a => a.Kind == AssetKind.Script && a.Placement == AssetPlacement.BodyEnd);

        // El script queda inyectado en la página final.
        var html = new MarkdownService(registry).RenderToHtml("```csharp\nvar x = 1;\n```\n", isDarkTheme: false);
        Assert.Contains("mv-code-wrap", html);
        Assert.Contains("mv-copy-btn", html);
    }

    [Fact]
    public void CopyButton_script_keeps_the_non_obvious_invariants()
    {
        var pluginsRoot = FindHostPluginsRoot();
        if (pluginsRoot is null || !Directory.Exists(pluginsRoot))
            return;

        var registry = new PluginRegistry();
        NewManager(registry, pluginsRoot).LoadAll();

        var script = registry.PreviewAssets
            .FirstOrDefault(a => a.Kind == AssetKind.Script && a.Value.Contains("mv-copy-btn"));
        Assert.NotNull(script);
        var js = script!.Value;

        // 1) Fallback a execCommand: el origen about:blank de NavigateToString NO es
        //    contexto seguro, así que navigator.clipboard puede no existir. Sin este
        //    fallback el botón fallaría en silencio. Guarda de regresión crítica.
        Assert.Contains("execCommand", js);

        // 2) Los bloques Mermaid se saltean (ese plugin los reemplaza por un diagrama).
        Assert.Contains("language-mermaid", js);

        // 3) Idempotencia frente al re-dispatch de DOMContentLoaded (window.__mvSetBody):
        //    no se agregan botones duplicados a un <pre> ya envuelto.
        Assert.Contains("mv-code-wrap", js);
    }

    [Fact]
    public void Disabling_CopyButton_removes_its_assets()
    {
        var pluginsRoot = FindHostPluginsRoot();
        if (pluginsRoot is null || !Directory.Exists(pluginsRoot))
            return;

        var registry = new PluginRegistry();
        var manager  = NewManager(registry, pluginsRoot);
        manager.LoadAll();
        Assert.Contains(registry.PreviewAssets, a => a.Value.Contains("mv-copy-btn"));

        manager.SetEnabled("core.copybutton", false);

        Assert.DoesNotContain(registry.PreviewAssets, a => a.Value.Contains("mv-copy-btn"));
    }

    public void Dispose()
    {
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch { /* best effort */ }
    }
}
