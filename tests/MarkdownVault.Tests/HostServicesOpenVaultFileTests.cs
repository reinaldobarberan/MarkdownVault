using System.IO;
using MarkdownVault.Services;
using MarkdownVault.Services.Plugins;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Cubre <see cref="HostServices.OpenVaultFile"/>: confinamiento al vault (reutiliza
/// <see cref="PathConfinement"/>, ya probado exhaustivamente en <c>PathConfinementTests</c>),
/// verificación de existencia, y la invocación del delegate de apertura inyectado en
/// composición (mismo patrón que <c>DarkThemeProvider</c>/<c>StatusSink</c> en <c>App.xaml.cs</c>).
/// No toca WPF/UI: el delegate de apertura es un <c>Action&lt;string&gt;</c> plano.
/// </summary>
public class HostServicesOpenVaultFileTests : IDisposable
{
    private readonly string _root;
    private readonly FileService _fileService;
    private readonly HostServices _host;

    public HostServicesOpenVaultFileTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"mvhost_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _fileService = new FileService();
        _fileService.OpenVault(_root);

        _host = new HostServices(_fileService);
    }

    [Fact]
    public void Existing_file_invokes_the_injected_open_action_with_the_resolved_full_path()
    {
        var relPath = "notes.md";
        File.WriteAllText(Path.Combine(_root, relPath), "hello");

        string? opened = null;
        _host.OpenFileAction = path => opened = path;

        _host.OpenVaultFile(relPath);

        Assert.Equal(Path.Combine(_root, relPath), opened);
    }

    [Fact]
    public void Nested_existing_file_invokes_the_open_action()
    {
        var dir = Path.Combine(_root, "sub");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "note.md"), "content");

        string? opened = null;
        _host.OpenFileAction = path => opened = path;

        _host.OpenVaultFile(Path.Combine("sub", "note.md"));

        Assert.Equal(Path.Combine(dir, "note.md"), opened);
    }

    [Fact]
    public void Missing_file_is_a_silent_no_op()
    {
        var invoked = false;
        _host.OpenFileAction = _ => invoked = true;

        var ex = Record.Exception(() => _host.OpenVaultFile("does-not-exist.md"));

        Assert.Null(ex);
        Assert.False(invoked);
    }

    [Fact]
    public void Path_escaping_the_vault_is_a_silent_no_op_not_a_throw()
    {
        var invoked = false;
        _host.OpenFileAction = _ => invoked = true;

        var ex = Record.Exception(() => _host.OpenVaultFile(Path.Combine("..", "..", "outside.md")));

        Assert.Null(ex);
        Assert.False(invoked);
    }

    [Fact]
    public void Absolute_path_escaping_the_vault_is_a_silent_no_op()
    {
        var invoked = false;
        _host.OpenFileAction = _ => invoked = true;

        var ex = Record.Exception(() => _host.OpenVaultFile(@"C:\Windows\System32\config"));

        Assert.Null(ex);
        Assert.False(invoked);
    }

    [Fact]
    public void No_vault_open_is_a_silent_no_op()
    {
        var host = new HostServices(new FileService());
        var invoked = false;
        host.OpenFileAction = _ => invoked = true;

        var ex = Record.Exception(() => host.OpenVaultFile("notes.md"));

        Assert.Null(ex);
        Assert.False(invoked);
    }

    [Fact]
    public void Unset_open_action_does_not_throw_even_for_a_valid_existing_file()
    {
        var relPath = "notes.md";
        File.WriteAllText(Path.Combine(_root, relPath), "hello");

        // _host.OpenFileAction is intentionally left null.
        var ex = Record.Exception(() => _host.OpenVaultFile(relPath));

        Assert.Null(ex);
    }

    public void Dispose()
    {
        _fileService.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
