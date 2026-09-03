using MarkdownVault.Plugin.Media;
using Xunit;

namespace MarkdownVault.Plugin.Media.Tests;

/// <summary>
/// Lo que el botón «Insertar video/audio…» escribe en el documento después de que
/// el usuario eligió un archivo. Sin diálogo y sin disco: solo la traducción de
/// ruta absoluta → enlace relativo, que es donde están todos los casos borde.
/// </summary>
public class MediaLinkBuilderTests
{
    private const string Vault = @"C:\vault";

    // ─── El camino feliz ─────────────────────────────────────────────────────

    [Fact]
    public void Builds_a_link_relative_to_the_vault_root()
    {
        var result = MediaLinkBuilder.Build(Vault, @"C:\vault\attachments\demo.mp4");

        Assert.True(result.Ok);
        Assert.Equal("![demo](attachments/demo.mp4)", result.Markdown);
    }

    /// <summary>
    /// Barras normales, no invertidas: el destino es una URL que resuelve la vista
    /// previa por vault.local, no una ruta de Windows.
    /// </summary>
    [Fact]
    public void Uses_forward_slashes_even_on_windows_paths()
    {
        var result = MediaLinkBuilder.Build(Vault, @"C:\vault\cursos\modulo 1\clase.mp4");

        Assert.Contains("cursos/modulo 1/clase.mp4", result.Markdown);
        Assert.DoesNotContain(@"\", result.Markdown);
    }

    /// <summary>
    /// CommonMark: un destino con espacios o paréntesis tiene que ir entre ángulos,
    /// si no el enlace directamente no se reconoce. Misma regla que aplica el host
    /// al convertir wikilinks.
    /// </summary>
    [Theory]
    [InlineData(@"C:\vault\mi video.mp4",     "![mi video](<mi video.mp4>)")]
    [InlineData(@"C:\vault\clip (final).mp4", "![clip (final)](<clip (final).mp4>)")]
    public void Wraps_destinations_that_need_angle_brackets(string chosen, string expected) =>
        Assert.Equal(expected, MediaLinkBuilder.Build(Vault, chosen).Markdown);

    [Fact]
    public void Plain_names_are_not_wrapped() =>
        Assert.Equal("![demo](demo.mp4)", MediaLinkBuilder.Build(Vault, @"C:\vault\demo.mp4").Markdown);

    // ─── Texto del corchete ──────────────────────────────────────────────────

    [Fact]
    public void Selection_becomes_the_description()
    {
        var result = MediaLinkBuilder.Build(Vault, @"C:\vault\demo.mp4", "La demo del módulo de pagos");

        Assert.Equal("![La demo del módulo de pagos](demo.mp4)", result.Markdown);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Without_a_selection_the_file_name_is_used(string? selection) =>
        Assert.Equal("![demo](demo.mp4)",
            MediaLinkBuilder.Build(Vault, @"C:\vault\demo.mp4", selection).Markdown);

    /// <summary>Un corchete suelto partiría el enlace en dos.</summary>
    [Fact]
    public void Brackets_in_the_description_are_escaped()
    {
        var result = MediaLinkBuilder.Build(Vault, @"C:\vault\demo.mp4", "clase [1]");

        Assert.Equal(@"![clase \[1\]](demo.mp4)", result.Markdown);
    }

    // ─── Lo que no se puede insertar ─────────────────────────────────────────

    /// <summary>
    /// EL caso que importa. La vista previa se sirve por vault.local, mapeado a la
    /// raíz del vault: un archivo de afuera no se sirve, punto. Más vale decirlo
    /// que insertar un enlace que se ve bien y no reproduce nada.
    /// </summary>
    [Theory]
    [InlineData(@"C:\otra-carpeta\demo.mp4")]   // hermana del vault
    [InlineData(@"C:\demo.mp4")]                // por encima del vault
    [InlineData(@"D:\videos\demo.mp4")]         // ni siquiera la misma unidad
    public void Refuses_files_outside_the_vault(string chosen)
    {
        var result = MediaLinkBuilder.Build(Vault, chosen);

        Assert.False(result.Ok);
        Assert.Contains("fuera del vault", result.Error);
        Assert.Contains("attachments", result.Error);   // dice QUÉ hacer, no solo que falló
    }

    /// <summary>
    /// Prefijo suelto: una carpeta del vault llamada "..copias" empieza con ".." y
    /// NO está afuera. Sin esta distinción, un nombre legítimo se rechazaría.
    /// </summary>
    [Fact]
    public void A_folder_whose_name_starts_with_dots_is_still_inside()
    {
        var result = MediaLinkBuilder.Build(Vault, @"C:\vault\..copias\demo.mp4");

        Assert.True(result.Ok);
        Assert.Equal("![demo](..copias/demo.mp4)", result.Markdown);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_an_open_vault_it_explains_why_it_cannot(string? vaultRoot)
    {
        var result = MediaLinkBuilder.Build(vaultRoot, @"C:\algo\demo.mp4");

        Assert.False(result.Ok);
        Assert.Contains("vault", result.Error);
    }

    [Fact]
    public void Empty_choice_is_rejected()
    {
        var result = MediaLinkBuilder.Build(Vault, "");

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }
}
