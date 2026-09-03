using System.Text.Json;

namespace MarkdownVault.Plugin.Media;

/// <summary>Qué reproductor le corresponde a un archivo enlazado como imagen.</summary>
public enum MediaKind
{
    /// <summary>No es medio: que lo renderice Markdig como siempre (&lt;img&gt;).</summary>
    None,
    Video,
    Audio
}

/// <summary>
/// Mapa extensión → tipo de reproductor, editable por el usuario. Vive en el
/// sandbox del plugin como <c>formatos.json</c>, mismo patrón que
/// <c>pronunciaciones.json</c> del Lector y <c>glosario.json</c> del Dictado:
/// agregar un formato NO exige recompilar.
///
/// Lógica PURA: sin I/O, testeable. El plugin es quien lee y escribe el archivo.
/// </summary>
public sealed class MediaFormats
{
    public const string FileName = "formatos.json";

    /// <summary>
    /// Solo formatos que WebView2 (Chromium/Edge) reproduce de verdad. NO están
    /// .mkv ni .avi a propósito: son CONTENEDORES que el motor no abre, y ponerlos
    /// acá daría un reproductor negro en vez de un enlace — peor que no hacer nada.
    /// El usuario puede agregarlos igual desde la ventana de Complementos; es su
    /// decisión, y el aviso bajo la lista se lo advierte.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Defaults =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Video — H.264/AAC y VP8/VP9 vienen con el runtime de Edge.
            [".mp4"]  = "video",
            [".m4v"]  = "video",
            [".webm"] = "video",
            [".ogv"]  = "video",
            // .mov reproduce SOLO si adentro trae H.264/AAC (el caso normal de un
            // teléfono o de QuickTime). Con códecs de edición —ProRes, HEVC— no.
            [".mov"]  = "video",

            // Audio.
            [".mp3"]  = "audio",
            [".m4a"]  = "audio",
            [".aac"]  = "audio",
            [".wav"]  = "audio",
            [".ogg"]  = "audio",
            [".oga"]  = "audio",
            [".opus"] = "audio",
            [".flac"] = "audio",
            // .opus y .m4a son los que deja el plugin de Dictado de Voz: cerrar el
            // círculo (grabo con un plugin, escucho con el otro) sale gratis.
            [".weba"] = "audio",
        };

    /// <summary>
    /// Formatos que el motor de la vista previa NO abre, aunque el usuario los
    /// declare. No se filtran —es su decisión— pero se avisan.
    /// </summary>
    public static readonly IReadOnlySet<string> KnownUnsupported =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".mkv", ".avi", ".wmv", ".flv", ".mpg", ".mpeg", ".rmvb", ".ts", ".wma" };

    // Estáticos y no expresiones de colección en línea: se recorren en cada enlace
    // de cada render, y acá no hace falta alocar un array por llamada.
    private static readonly char[] QueryOrFragment = ['?', '#'];
    private static readonly char[] PathSeparators  = ['/', '\\'];

    private readonly Dictionary<string, MediaKind> _map;
    private readonly Dictionary<string, string>    _raw;

    private MediaFormats(IEnumerable<KeyValuePair<string, string>> entries)
    {
        _map = new Dictionary<string, MediaKind>(StringComparer.OrdinalIgnoreCase);
        _raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in entries)
        {
            var ext = Normalize(e.Key);
            if (ext is null) continue;

            // Se CONSERVA la fila aunque el tipo esté vacío o mal escrito. Igual que
            // en PronunciationDictionary: una fila a medio llenar es trabajo en curso,
            // no una regla — borrarla al guardar haría desaparecer de la lista lo que
            // el usuario acaba de escribir. Se guarda, se muestra, y no reproduce nada
            // hasta que diga "video" o "audio".
            var value = e.Value?.Trim() ?? "";
            _raw[ext] = value;

            var kind = ParseKind(value);
            if (kind != MediaKind.None) _map[ext] = kind;
        }
    }

    /// <summary>
    /// Deja la extensión en forma canónica: con punto y en minúscula. Acepta tanto
    /// <c>mp4</c> como <c>.MP4</c> — el usuario escribe en la ventana de
    /// Complementos, no en un archivo de configuración, y ahí nadie se acuerda del
    /// punto. Devuelve <c>null</c> si no queda nada usable.
    /// </summary>
    public static string? Normalize(string? ext)
    {
        var s = ext?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        if (s[0] != '.') s = "." + s;
        return s.Length > 1 ? s.ToLowerInvariant() : null;
    }

    private static MediaKind ParseKind(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "video" => MediaKind.Video,
        "audio" => MediaKind.Audio,
        _       => MediaKind.None
    };

    public static MediaFormats Empty { get; } = new(Array.Empty<KeyValuePair<string, string>>());

    public static MediaFormats Default { get; } = new(Defaults);

    public static MediaFormats From(IEnumerable<KeyValuePair<string, string>> entries) => new(entries);

    /// <summary>Las filas tal como las ve el usuario, incluidas las incompletas.</summary>
    public IReadOnlyDictionary<string, string> Entries => _raw;

    public int Count => _raw.Count;

    /// <summary>Filas sin tipo válido: están guardadas pero no reproducen nada.</summary>
    public int IncompleteCount => _raw.Count - _map.Count;

    /// <summary>
    /// Las extensiones ACTIVAS de un tipo, ordenadas. La usa el plugin para armar
    /// el filtro del diálogo de archivos: así el "Elegir video…" ofrece exactamente
    /// lo que la lista dice que se puede reproducir, ni más ni menos.
    /// </summary>
    public IReadOnlyList<string> ExtensionsOf(MediaKind kind) =>
        _map.Where(kv => kv.Value == kind)
            .Select(kv => kv.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

    /// <summary>Filas activas cuyo formato el motor de la vista previa no sabe abrir.</summary>
    public IReadOnlyList<string> UnsupportedDeclared =>
        _map.Keys.Where(KnownUnsupported.Contains)
                 .OrderBy(k => k, StringComparer.Ordinal)
                 .ToList();

    /// <summary>
    /// Qué reproductor le toca a <paramref name="url"/>, o <see cref="MediaKind.None"/>
    /// si no es un medio declarado.
    ///
    /// SOLO rutas locales del vault. Una URL absoluta (http, https, data:, //cdn…)
    /// se deja pasar a propósito: el alcance acordado es local, y un &lt;video&gt;
    /// apuntando afuera abriría una petición de red desde la vista previa sin que
    /// nadie lo haya pedido. Para habilitarlas alcanza con sacar la guarda
    /// <see cref="IsRemote"/>.
    /// </summary>
    public MediaKind Resolve(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return MediaKind.None;
        if (IsRemote(url)) return MediaKind.None;

        // Se corta en '?' y '#': "demo.mp4?v=2" sigue siendo un .mp4.
        var path = url;
        int cut = path.IndexOfAny(QueryOrFragment);
        if (cut >= 0) path = path[..cut];

        int dot = path.LastIndexOf('.');
        if (dot < 0 || dot == path.Length - 1) return MediaKind.None;

        // Un punto anterior a la última barra no es una extensión:
        // "v1.2/clip" no es un archivo ".2/clip".
        if (path.IndexOfAny(PathSeparators, dot) >= 0) return MediaKind.None;

        return _map.TryGetValue(path[dot..], out var kind) ? kind : MediaKind.None;
    }

    /// <summary>
    /// ¿La URL apunta afuera del vault? Cubre esquemas (<c>http:</c>, <c>data:</c>),
    /// protocolo relativo (<c>//cdn/…</c>) y rutas absolutas de Windows (<c>C:\…</c>),
    /// que el host tampoco sirve por <c>vault.local</c>.
    /// </summary>
    private static bool IsRemote(string url)
    {
        if (url.StartsWith("//", StringComparison.Ordinal)) return true;

        int colon = url.IndexOf(':');
        if (colon <= 0) return false;

        // Un esquema válido no tiene separadores antes de los dos puntos. Se recorre
        // a mano en vez de usar IndexOfAny sobre un span: son cuatro caracteres, y
        // así no queda colgado de cuál sobrecarga elige el compilador.
        for (int i = 0; i < colon; i++)
        {
            if (url[i] is '/' or '\\' or '?' or '#') return false;
        }

        return true;
    }

    // ─── JSON ────────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Lee <c>formatos.json</c>. Un archivo roto NO tumba el plugin: se avisa por el
    /// log y se cae a <see cref="Default"/>, que es exactamente lo que el usuario
    /// esperaría ver si nunca hubiera tocado el archivo.
    /// </summary>
    public static MediaFormats Parse(string json, Action<string>? log = null)
    {
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return map is null ? Default : new MediaFormats(map);
        }
        catch (JsonException ex)
        {
            log?.Invoke($"formatos.json ilegible ({ex.Message}); se usan los formatos por defecto.");
            return Default;
        }
    }

    public static string DefaultsAsJson() => JsonSerializer.Serialize(Defaults, WriteOpts);

    public string ToJson() => JsonSerializer.Serialize(
        _raw.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(kv => kv.Key, kv => kv.Value),
        WriteOpts);
}
