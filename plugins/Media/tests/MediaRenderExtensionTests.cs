using Markdig;
using MarkdownVault.Plugin.Media;
using Xunit;

namespace MarkdownVault.Plugin.Media.Tests;

/// <summary>
/// Integración de la extensión de Markdig con un pipeline REAL (no simulado), por el
/// mismo motivo que documentan los tests de Eisenhower: en Markdig 1.1.2 el dispatch
/// va por asignabilidad de TIPO y <c>Accept</c> es <c>sealed</c>, así que insertar en
/// el índice 0 convierte a nuestro renderer en el ÚNICO candidato para todo
/// <see cref="Markdig.Syntax.Inlines.LinkInline"/> — enlaces comunes e imágenes
/// incluidos. Los tests de "esto NO se toca" de esta clase son, por lo tanto, la
/// guarda de regresión más importante del plugin: si el fallback se rompe, se rompen
/// TODOS los enlaces y TODAS las imágenes de la vista previa, no solo los medios.
/// </summary>
public class MediaRenderExtensionTests
{
    /// <summary>Réplica del pipeline del host (MarkdownService.GetPipeline) más nuestra extensión.</summary>
    private static string Render(string markdown, MediaFormats? formats = null)
    {
        var resolved = formats ?? MediaFormats.Default;

        var builder = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseAutoIdentifiers(Markdig.Extensions.AutoIdentifiers.AutoIdentifierOptions.GitHub);

        builder.Extensions.Add(new MediaMarkdigExtension(() => resolved));

        return Markdown.ToHtml(markdown, builder.Build());
    }

    // ─── Lo que SÍ se convierte ──────────────────────────────────────────────

    [Fact]
    public void Image_pointing_at_a_video_becomes_a_player()
    {
        var html = Render("![](attachments/demo.mp4)");

        Assert.Contains("<video", html);
        Assert.Contains("controls", html);
        Assert.Contains("src=\"attachments/demo.mp4\"", html);
        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public void Image_pointing_at_an_audio_becomes_a_player()
    {
        var html = Render("![](attachments/nota.mp3)");

        Assert.Contains("<audio", html);
        Assert.Contains("controls", html);
        Assert.DoesNotContain("<video", html);
        Assert.DoesNotContain("<img", html);
    }

    /// <summary>
    /// La forma a la que el host reduce un wikilink de incrustación. El regex de
    /// <c>MarkdownService.PreprocessWikiLinks</c> solo captura <c>[[…]]</c>, así que
    /// el <c>!</c> sobrevive y <c>![[demo.mp4]]</c> llega a Markdig ya convertido en
    /// esto. Es la razón por la que alcanza con UN punto de intercepción.
    ///
    /// Se prueba la forma resultante y no el wikilink crudo a propósito: la
    /// reescritura vive en el host, y estos tests no referencian al host (ver el
    /// comentario del .csproj).
    /// </summary>
    [Fact]
    public void Wikilink_embed_shape_also_becomes_a_player()
    {
        var html = Render("![demo](demo.mp4)");

        Assert.Contains("<video", html);
        Assert.Contains("src=\"demo.mp4\"", html);
    }

    /// <summary>El texto del corchete es el contenido de respaldo del reproductor.</summary>
    [Fact]
    public void Alt_text_becomes_the_fallback_content()
    {
        var html = Render("![Demo del módulo de pagos](demo.mp4)");

        Assert.Contains(">Demo del módulo de pagos</video>", html);
    }

    [Fact]
    public void Player_does_not_preload_the_whole_file()
    {
        // Cinco videos de 200 MB no pueden costar 1 GB de disco leído en cada tecla.
        Assert.Contains("preload=\"metadata\"", Render("![](demo.mp4)"));
    }

    [Fact]
    public void Player_carries_the_class_the_css_and_js_hook_onto()
    {
        Assert.Contains(MediaLinkRenderer.CssClass, Render("![](demo.mp4)"));
    }

    // ─── Lo que NO se toca (guardas de regresión) ────────────────────────────

    [Fact]
    public void Real_images_still_render_as_images()
    {
        var html = Render("![gato](image/gato.png)");

        Assert.Contains("<img", html);
        Assert.Contains("src=\"image/gato.png\"", html);
        Assert.Contains("alt=\"gato\"", html);
        Assert.DoesNotContain("<video", html);
    }

    /// <summary>
    /// Sin el '!' es un ENLACE, y tiene que seguir siéndolo: quien escribe
    /// [mirá el clip](demo.mp4) quiere un enlace, no un reproductor incrustado.
    /// </summary>
    [Fact]
    public void Plain_link_to_a_video_stays_a_link()
    {
        var html = Render("[mirá el clip](demo.mp4)");

        Assert.Contains("<a href=\"demo.mp4\"", html);
        Assert.DoesNotContain("<video", html);
    }

    [Fact]
    public void Plain_links_still_render()
    {
        var html = Render("[otra nota](otra.md) y [afuera](https://ejemplo.com)");

        Assert.Contains("href=\"otra.md\"", html);
        Assert.Contains("href=\"https://ejemplo.com\"", html);
    }

    /// <summary>Guarda del alcance acordado: solo archivos locales del vault.</summary>
    [Fact]
    public void Remote_video_url_is_left_as_an_image()
    {
        var html = Render("![](https://ejemplo.com/demo.mp4)");

        Assert.Contains("<img", html);
        Assert.DoesNotContain("<video", html);
    }

    // ─── La lista editable manda ─────────────────────────────────────────────

    /// <summary>
    /// El renderer lee los formatos a través de una FUNCIÓN, no de un valor
    /// capturado: si no fuese así, editar la lista en la ventana de Complementos no
    /// surtiría efecto hasta reiniciar, porque MarkdownService cachea el pipeline.
    /// </summary>
    [Fact]
    public void Renderer_reads_the_current_formats_not_a_snapshot()
    {
        var formats = MediaFormats.From(new[] { new KeyValuePair<string, string>(".mp4", "video") });

        var builder = new MarkdownPipelineBuilder();
        builder.Extensions.Add(new MediaMarkdigExtension(() => formats));
        var pipeline = builder.Build();

        Assert.DoesNotContain("<video", Markdown.ToHtml("![](clip.xyz)", pipeline));

        // El usuario agrega .xyz desde la ventana de Complementos…
        formats = MediaFormats.From(new[] { new KeyValuePair<string, string>(".mp4", "video"), new KeyValuePair<string, string>(".xyz", "video") });

        // …y el MISMO pipeline ya cacheado lo respeta.
        Assert.Contains("<video", Markdown.ToHtml("![](clip.xyz)", pipeline));
    }

    [Fact]
    public void Format_removed_from_the_list_stops_being_a_player()
    {
        var sinVideo = MediaFormats.From(new[] { new KeyValuePair<string, string>(".mp3", "audio") });

        Assert.Contains("<img", Render("![](demo.mp4)", sinVideo));
        Assert.DoesNotContain("<video", Render("![](demo.mp4)", sinVideo));
    }
}
