using MarkdownVault.Plugin.Media;
using MarkdownVault.PluginSdk;
using Xunit;

namespace MarkdownVault.Plugin.Media.Tests;

/// <summary>
/// Contra QUÉ raíz se calcula el enlace que escribe el botón de la barra.
///
/// Es la guarda del bug que motivó <see cref="IHostServices.GetOwningRoot"/> (SDK
/// 1.5.0): la vista previa mapea <c>vault.local</c> a la raíz que POSEE la nota
/// activa —el prefijo más largo cuando hay raíces anidadas— y no a la primera
/// abierta. Calcular contra la primera producía un enlace bien formado que no
/// resolvía.
/// </summary>
public class ActiveRootTests
{
    /// <summary>Host mínimo: solo lo que <c>ActiveRoot</c> consulta.</summary>
    private sealed class FakeHost : IHostServices
    {
        public string? VaultRoot      { get; init; }
        public string? ActiveFilePath { get; init; }

        /// <summary>Lo que el host respondería para la nota activa. <c>null</c> = afuera de toda raíz.</summary>
        public string? OwningRoot { get; init; }

        public string? GetOwningRoot(string path) => OwningRoot;

        public bool IsDarkTheme => false;
        public Task<string> ReadFileAsync(string relativePath) => Task.FromResult("");
        public void ShowStatus(string message) { }
        public void OpenVaultFile(string relativePath) { }
        public IProgressScope BeginProgress(string title) => NoOpProgressScope.Instance;
    }

    /// <summary>
    /// EL caso del bug: dos raíces, una adentro de la otra, y la nota activa en la
    /// de adentro. Gana la que la posee, no la primera abierta.
    /// </summary>
    [Fact]
    public void Nested_roots_resolve_to_the_one_that_owns_the_active_note()
    {
        var host = new FakeHost
        {
            VaultRoot      = @"C:\vault",
            ActiveFilePath = @"C:\vault\proyecto\nota.md",
            OwningRoot     = @"C:\vault\proyecto"
        };

        Assert.Equal(@"C:\vault\proyecto", MediaPlugin.ActiveRoot(host));
    }

    [Fact]
    public void Single_root_resolves_to_that_root()
    {
        var host = new FakeHost
        {
            VaultRoot      = @"C:\vault",
            ActiveFilePath = @"C:\vault\nota.md",
            OwningRoot     = @"C:\vault"
        };

        Assert.Equal(@"C:\vault", MediaPlugin.ActiveRoot(host));
    }

    /// <summary>Sin nota activa todavía no hay a quién preguntarle: se usa la raíz superior.</summary>
    [Fact]
    public void Falls_back_to_the_top_root_when_there_is_no_active_note()
    {
        var host = new FakeHost { VaultRoot = @"C:\vault", ActiveFilePath = null };

        Assert.Equal(@"C:\vault", MediaPlugin.ActiveRoot(host));
    }

    /// <summary>
    /// Un archivo suelto, fuera de toda raíz abierta: el host devuelve null y se cae
    /// a la raíz superior. Si tampoco hay, MediaLinkBuilder es quien lo explica.
    /// </summary>
    [Fact]
    public void Falls_back_to_the_top_root_when_the_note_belongs_to_no_root()
    {
        var host = new FakeHost
        {
            VaultRoot      = @"C:\vault",
            ActiveFilePath = @"C:\otra-parte\suelta.md",
            OwningRoot     = null
        };

        Assert.Equal(@"C:\vault", MediaPlugin.ActiveRoot(host));
    }

    [Fact]
    public void Without_any_vault_there_is_no_root()
    {
        Assert.Null(MediaPlugin.ActiveRoot(new FakeHost()));
    }

    /// <summary>
    /// El enlace que sale de la raíz correcta. Antes del arreglo, con estos mismos
    /// datos, el botón escribía "proyecto/attachments/demo.mp4" y la vista previa
    /// buscaba C:\vault\proyecto\proyecto\attachments\demo.mp4.
    /// </summary>
    [Fact]
    public void End_to_end_the_link_is_relative_to_the_owning_root()
    {
        var host = new FakeHost
        {
            VaultRoot      = @"C:\vault",
            ActiveFilePath = @"C:\vault\proyecto\nota.md",
            OwningRoot     = @"C:\vault\proyecto"
        };

        var result = MediaLinkBuilder.Build(
            MediaPlugin.ActiveRoot(host), @"C:\vault\proyecto\attachments\demo.mp4");

        Assert.Equal("![demo](attachments/demo.mp4)", result.Markdown);
    }
}
