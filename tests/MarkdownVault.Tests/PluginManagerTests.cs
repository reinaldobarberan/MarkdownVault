using System.IO;
using MarkdownVault.Services;
using MarkdownVault.Services.Plugins;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Cubre el descubrimiento y la validación del PluginManager usando una carpeta
/// temporal de plugins y un SettingsService aislado. No cubre la ACTIVACIÓN
/// exitosa (requiere un DLL real; validado end-to-end con el plugin Mermaid).
/// </summary>
public class PluginManagerTests : IDisposable
{
    private readonly string          _root;
    private readonly PluginRegistry  _registry = new();
    private readonly SettingsService _settings;

    public PluginManagerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"mvtest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _settings = new SettingsService(Path.Combine(_root, "settings.json"));
    }

    private PluginManager NewManager(string? root = null) =>
        new(_registry, new FakeHost(), _settings, root ?? _root);

    private void WriteManifest(string folder, string json)
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.json"), json);
    }

    [Fact]
    public void Nonexistent_folder_yields_no_plugins()
    {
        var mgr = NewManager(Path.Combine(_root, "does-not-exist"));
        mgr.LoadAll();
        Assert.Empty(mgr.Plugins);
    }

    [Fact]
    public void Subfolder_without_manifest_is_ignored()
    {
        Directory.CreateDirectory(Path.Combine(_root, "NoManifest"));
        var mgr = NewManager();

        mgr.LoadAll();

        Assert.Empty(mgr.Plugins);
    }

    [Fact]
    public void Invalid_json_marks_plugin_failed()
    {
        WriteManifest("Bad", "{ not valid json ");
        var mgr = NewManager();

        mgr.LoadAll();

        var p = Assert.Single(mgr.Plugins);
        Assert.Equal(PluginState.Failed, p.State);
        Assert.False(p.Enabled);
    }

    [Fact]
    public void Missing_required_field_marks_plugin_failed()
    {
        WriteManifest("NoEntry", """{ "id": "x.y", "name": "X", "version": "1.0.0" }""");
        var mgr = NewManager();

        mgr.LoadAll();

        var p = Assert.Single(mgr.Plugins);
        Assert.Equal(PluginState.Failed, p.State);
        Assert.Contains("entry", p.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Incompatible_minSdk_marks_plugin_failed()
    {
        WriteManifest("Future",
            """{ "id": "x.future", "name": "F", "version": "1.0.0", "entry": "F.dll", "minSdk": "99.0.0" }""");
        var mgr = NewManager();

        mgr.LoadAll();

        var p = Assert.Single(mgr.Plugins);
        Assert.Equal(PluginState.Failed, p.State);
        Assert.Contains("SDK", p.Error!);
    }

    [Fact]
    public void MinSdk_1_1_0_plugin_is_accepted_by_1_1_0_host()
    {
        WriteManifest("Modern",
            """{ "id": "x.modern", "name": "M", "version": "1.0.0", "entry": "Modern.dll", "minSdk": "1.1.0" }""");
        var mgr = NewManager();

        mgr.LoadAll();

        var p = Assert.Single(mgr.Plugins);
        // El DLL sigue faltando (no es el foco de este test), así que queda Failed —
        // pero el motivo NO debe ser incompatibilidad de SDK: minSdk 1.1.0 debe pasar
        // la verificación de compatibilidad contra un host que provee SDK 1.1.0.
        Assert.Equal(PluginState.Failed, p.State);
        Assert.DoesNotContain("SDK", p.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DLL", p.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Valid_manifest_with_missing_dll_marks_plugin_failed()
    {
        WriteManifest("Ghost",
            """{ "id": "x.ghost", "name": "G", "version": "1.0.0", "entry": "Ghost.dll" }""");
        var mgr = NewManager();

        mgr.LoadAll();

        var p = Assert.Single(mgr.Plugins);
        Assert.Equal(PluginState.Failed, p.State);
        Assert.Contains("DLL", p.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Metadata_is_read_from_manifest()
    {
        WriteManifest("Meta",
            """{ "id": "x.meta", "name": "Nice Name", "version": "2.3.4", "author": "Me", "description": "Desc", "entry": "m.dll" }""");
        var mgr = NewManager();

        mgr.LoadAll();

        var p = Assert.Single(mgr.Plugins);
        Assert.Equal("x.meta",    p.Metadata.Id);
        Assert.Equal("Nice Name", p.Metadata.Name);
        Assert.Equal("2.3.4",     p.Metadata.Version);
        Assert.Equal("Me",        p.Metadata.Author);
    }

    [Fact]
    public void SetEnabled_ignores_non_active_plugin()
    {
        WriteManifest("Ghost",
            """{ "id": "x.ghost", "name": "G", "version": "1.0.0", "entry": "Ghost.dll" }""");
        var mgr = NewManager();
        mgr.LoadAll();   // queda Failed (DLL ausente)

        mgr.SetEnabled("x.ghost", true);

        Assert.False(_registry.IsEnabled("x.ghost"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
