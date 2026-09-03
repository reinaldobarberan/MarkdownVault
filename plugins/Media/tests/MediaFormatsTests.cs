using MarkdownVault.Plugin.Media;
using Xunit;

namespace MarkdownVault.Plugin.Media.Tests;

/// <summary>
/// Lógica PURA del mapa extensión → reproductor. Sin Markdig, sin disco, sin host.
/// </summary>
public class MediaFormatsTests
{
    private static MediaFormats Sut(params (string Ext, string Kind)[] rows) =>
        MediaFormats.From(rows.Select(r => new KeyValuePair<string, string>(r.Ext, r.Kind)));

    // ─── Resolución por extensión ────────────────────────────────────────────

    [Theory]
    [InlineData("demo.mp4")]
    [InlineData("attachments/demo.mp4")]
    [InlineData("sub/carpeta/demo.MP4")]          // la extensión no distingue mayúsculas
    [InlineData("demo.mp4?v=2")]                   // query string
    [InlineData("demo.mp4#t=30")]                  // fragmento
    public void Resolves_video_by_extension(string url) =>
        Assert.Equal(MediaKind.Video, MediaFormats.Default.Resolve(url));

    [Theory]
    [InlineData("nota.mp3")]
    [InlineData("attachments/nota-de-voz.opus")]   // lo que deja el plugin de Dictado
    [InlineData("grabacion.m4a")]
    public void Resolves_audio_by_extension(string url) =>
        Assert.Equal(MediaKind.Audio, MediaFormats.Default.Resolve(url));

    [Theory]
    [InlineData("foto.png")]
    [InlineData("diagrama.svg")]
    [InlineData("nota.md")]
    [InlineData("sin-extension")]
    [InlineData("termina-en-punto.")]
    [InlineData("")]
    [InlineData(null)]
    public void Leaves_non_media_alone(string? url) =>
        Assert.Equal(MediaKind.None, MediaFormats.Default.Resolve(url));

    /// <summary>
    /// Un punto que quedó ANTES de la última barra no es una extensión. Sin esta
    /// guarda, "v1.2/clip" se leería como un archivo ".2/clip".
    /// </summary>
    [Fact]
    public void Dot_before_a_slash_is_not_an_extension() =>
        Assert.Equal(MediaKind.None, MediaFormats.Default.Resolve("v1.2/clip"));

    // ─── Alcance: SOLO local ─────────────────────────────────────────────────

    /// <summary>
    /// Guarda del alcance acordado. Si alguien habilita URLs remotas, este test cae
    /// primero y obliga a que sea una decisión y no un accidente.
    /// </summary>
    [Theory]
    [InlineData("https://ejemplo.com/demo.mp4")]
    [InlineData("http://ejemplo.com/demo.mp4")]
    [InlineData("//cdn.ejemplo.com/demo.mp4")]
    [InlineData("data:video/mp4;base64,AAAA.mp4")]
    [InlineData("C:\\Videos\\demo.mp4")]
    public void Remote_urls_are_left_to_the_default_renderer(string url) =>
        Assert.Equal(MediaKind.None, MediaFormats.Default.Resolve(url));

    /// <summary>Una ruta relativa con dos puntos adentro NO es remota.</summary>
    [Fact]
    public void Relative_path_containing_a_colon_is_still_local() =>
        Assert.Equal(MediaKind.Video, MediaFormats.Default.Resolve("notas/2024:reunion/demo.mp4"));

    // ─── Normalización de la extensión ───────────────────────────────────────

    [Theory]
    [InlineData("mp4", ".mp4")]      // el usuario se olvida del punto
    [InlineData(".MP4", ".mp4")]
    [InlineData("  .mkv  ", ".mkv")]
    public void Normalize_canonicalises_the_extension(string input, string expected) =>
        Assert.Equal(expected, MediaFormats.Normalize(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData(null)]
    public void Normalize_rejects_what_is_not_an_extension(string? input) =>
        Assert.Null(MediaFormats.Normalize(input));

    [Fact]
    public void Entries_written_without_a_dot_still_resolve()
    {
        var sut = Sut(("mp4", "video"));
        Assert.Equal(MediaKind.Video, sut.Resolve("demo.mp4"));
    }

    // ─── Filas incompletas ───────────────────────────────────────────────────

    /// <summary>
    /// Una fila a medio llenar es trabajo en curso, no una regla: se GUARDA y se
    /// muestra —borrarla haría desaparecer de la lista lo que el usuario acaba de
    /// escribir— pero no reproduce nada. Mismo criterio que PronunciationDictionary.
    /// </summary>
    [Fact]
    public void Rows_without_a_type_are_kept_but_inert()
    {
        var sut = Sut((".mp4", "video"), (".xyz", ""), (".abc", "peliculita"));

        Assert.Equal(3, sut.Count);
        Assert.Equal(2, sut.IncompleteCount);
        Assert.Equal(MediaKind.None, sut.Resolve("a.xyz"));
        Assert.Equal(MediaKind.None, sut.Resolve("a.abc"));
        Assert.Contains(".xyz", sut.Entries.Keys);
    }

    [Fact]
    public void Type_is_case_insensitive()
    {
        var sut = Sut((".mp4", "VIDEO"), (".mp3", " Audio "));
        Assert.Equal(MediaKind.Video, sut.Resolve("a.mp4"));
        Assert.Equal(MediaKind.Audio, sut.Resolve("a.mp3"));
    }

    // ─── Aviso de formatos que el motor no abre ──────────────────────────────

    [Fact]
    public void Flags_containers_the_preview_engine_cannot_open()
    {
        var sut = Sut((".mp4", "video"), (".mkv", "video"), (".avi", "video"));
        Assert.Equal(new[] { ".avi", ".mkv" }, sut.UnsupportedDeclared);
    }

    /// <summary>Declarado pero inservible sigue sin avisarse si la fila está inerte.</summary>
    [Fact]
    public void Inert_rows_are_not_flagged_as_unsupported()
    {
        var sut = Sut((".mkv", ""));
        Assert.Empty(sut.UnsupportedDeclared);
    }

    [Fact]
    public void Defaults_do_not_ship_a_format_the_engine_cannot_open() =>
        Assert.Empty(MediaFormats.Default.UnsupportedDeclared);

    // ─── JSON ────────────────────────────────────────────────────────────────

    [Fact]
    public void Round_trips_through_json()
    {
        var original = Sut((".mp4", "video"), (".mp3", "audio"), (".xyz", ""));
        var reparsed = MediaFormats.Parse(original.ToJson());

        Assert.Equal(original.Entries, reparsed.Entries);
        Assert.Equal(MediaKind.Video, reparsed.Resolve("a.mp4"));
    }

    /// <summary>
    /// Un formatos.json roto NO puede tumbar el plugin ni dejar la vista previa sin
    /// reproductores: se avisa y se cae a los formatos de fábrica.
    /// </summary>
    [Fact]
    public void Broken_json_falls_back_to_defaults_and_reports()
    {
        string? logged = null;
        var sut = MediaFormats.Parse("{ esto no es json", msg => logged = msg);

        Assert.NotNull(logged);
        Assert.Equal(MediaKind.Video, sut.Resolve("a.mp4"));
    }

    [Fact]
    public void Seed_file_parses_back_into_the_defaults()
    {
        var sut = MediaFormats.Parse(MediaFormats.DefaultsAsJson());
        Assert.Equal(MediaFormats.Default.Entries, sut.Entries);
    }
}
