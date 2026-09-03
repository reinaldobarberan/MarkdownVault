using System.IO;
using MarkdownVault.PluginSdk;
using Microsoft.Win32;

namespace MarkdownVault.Plugin.Media;

/// <summary>
/// Reproduce video y audio del vault dentro de la vista previa: <c>![](clip.mp4)</c>
/// y <c>![[nota-de-voz.opus]]</c> dejan de ser una imagen rota y pasan a ser un
/// reproductor con controles.
///
/// No toca el núcleo. Usa los tres puntos de extensión que ya existían:
/// una extensión de Markdig (como Callouts y Eisenhower), PreviewAssets de CSS y JS
/// (como Highlight y CopyButton) y una lista editable dibujada por el host
/// (como el glosario del Dictado).
///
/// Alcance deliberado: SOLO archivos locales del vault. Ver <see cref="MediaFormats.Resolve"/>.
/// </summary>
public sealed class MediaPlugin : IPlugin
{
    private IPluginContext? _context;

    /// <summary>
    /// Los formatos vigentes. Es un CAMPO mutable y no un valor capturado porque la
    /// extensión de Markdig lo lee a través de una función: así, editar la lista en
    /// la ventana de Complementos surte efecto en el siguiente render, sin reiniciar.
    /// </summary>
    private MediaFormats _formats = MediaFormats.Default;

    public void Configure(IPluginContext context)
    {
        _context = context;
        _formats = LoadFormats(context);

        // 1) Render: la imagen que apunta a un medio se convierte en reproductor.
        context.AddMarkdownExtension(new MediaContribution(() => _formats));

        // 2) Estilos del reproductor (head).
        context.AddPreviewAsset(new PreviewAsset
        {
            Kind      = AssetKind.Style,
            Source    = AssetSource.Inline,
            Value     = Css,
            Placement = AssetPlacement.HeadEnd
        });

        // 3) Conservar la reproducción entre actualizaciones de la vista previa (fin del body).
        context.AddPreviewAsset(new PreviewAsset
        {
            Kind      = AssetKind.Script,
            Source    = AssetSource.Inline,
            Value     = KeepAliveScript,
            Placement = AssetPlacement.BodyEnd
        });

        // 4) Lista editable: qué extensión es video y cuál es audio.
        context.AddListSetting(new PluginListSetting
        {
            Id          = "core.media.formatos",
            Title       = "Formatos reproducibles",
            Description = "Qué extensiones se muestran como reproductor en vez de como imagen. " +
                          "Escribí «video» o «audio» en la segunda columna; cualquier otra cosa " +
                          "deja el archivo como estaba. Se guarda en formatos.json.",
            KeyLabel    = "Extensión",
            ValueLabel  = "Tipo",
            Load        = LoadEntries,
            Save        = SaveEntries,
            Describe    = Describe
        });

        // 5) Botón de barra: elegir el archivo y que el enlace lo escriba el plugin.
        context.AddCommandGroup(new PluginCommandGroup
        {
            Id    = "core.media.insertar",
            Title = "Medios",
            Icon  = "🎬",
            Commands =
            [
                Command("video", "Insertar video…", "🎬", MediaKind.Video),
                Command("audio", "Insertar audio…", "🎧", MediaKind.Audio)
            ]
        });

        context.Log($"Medios registrado: {_formats.Count} formatos " +
                    $"({_formats.ExtensionsOf(MediaKind.Video).Count} video / " +
                    $"{_formats.ExtensionsOf(MediaKind.Audio).Count} audio).");
    }

    // ─── Botón de barra: elegir archivo e insertar el enlace ──────────────────

    private PluginCommand Command(string suffix, string title, string icon, MediaKind kind) => new()
    {
        Id      = $"core.media.insertar.{suffix}",
        Title   = title,
        Icon    = icon,
        Execute = editor => PickAndInsert(editor, kind)
    };

    /// <summary>
    /// Abre el diálogo de archivos y escribe el enlace ya resuelto contra la raíz
    /// del vault. Sin ventana propia y sin <c>owner</c>, igual que el plugin de
    /// Dictado: declarar un tipo <c>Window</c> clava el contexto de carga del plugin
    /// y le hace perder la descarga en caliente (documentado en GUIA-PLUGINS.md).
    /// Un <see cref="OpenFileDialog"/> no es una <c>Window</c>, así que no paga ese precio.
    /// </summary>
    private void PickAndInsert(IEditorContext editor, MediaKind kind)
    {
        var context = _context;
        if (context is null) return;

        // Se anota la selección ANTES de abrir el diálogo: es modal y le roba el
        // foco al editor. Mismo cuidado que toma el plugin de Dictado.
        var selected  = editor.SelectedText;
        var vaultRoot = ActiveRoot(context.Host);

        var esVideo = kind == MediaKind.Video;

        var dialog = new OpenFileDialog
        {
            Title            = esVideo ? "Elegir un video del vault" : "Elegir un audio del vault",
            Filter           = BuildFilter(kind),
            CheckFileExists  = true,
            InitialDirectory = StartingFolder(vaultRoot)
        };

        if (dialog.ShowDialog() != true) return;

        // Si había algo seleccionado, ese texto pasa a ser la descripción del medio
        // y el enlace lo reemplaza. Es el mismo trato que le da el host a
        // «Insertar enlace interno» sobre una selección.
        var result = MediaLinkBuilder.Build(vaultRoot, dialog.FileName, selected);

        if (!result.Ok)
        {
            context.Host.ShowStatus(result.Error!);
            return;
        }

        if (string.IsNullOrEmpty(selected))
        {
            // Salto de línea AL FINAL, y no es un detalle estético. El contrato del
            // host abre línea nueva ANTES del caret si hace falta, pero no cierra
            // atrás: insertando al principio de una línea que ya tenía texto, el
            // enlace queda pegado a lo que seguía. Verificado en la app: insertar
            // sobre «# Título» dejaba «![demo](x.mp4)# Título» — y el encabezado
            // dejaba de ser encabezado. Un medio es un bloque; tiene que terminar
            // su línea.
            editor.InsertAtCaret(result.Markdown + "\n");
        }
        else
        {
            // Reemplazando una selección NO se agrega nada: la selección puede estar
            // en medio de una frase y el usuario ya eligió dónde empieza y termina.
            editor.ReplaceSelection(result.Markdown!);
        }
    }

    /// <summary>
    /// La raíz contra la que hay que calcular el enlace: la que POSEE la nota
    /// activa, no la primera abierta.
    ///
    /// No es un detalle. La vista previa mapea <c>vault.local</c> a
    /// <c>GetOwningRoot(nota activa)</c>, que con raíces anidadas es el prefijo MÁS
    /// LARGO. Calcular contra <c>VaultRoot</c> (la primera) con dos raíces —
    /// <c>C:\vault</c> y <c>C:\vault\proyecto</c>— y una nota en la segunda daba
    /// <c>proyecto/attachments/demo.mp4</c>, que la vista previa resolvía como
    /// <c>C:\vault\proyecto\proyecto\attachments\demo.mp4</c>: enlace bien formado
    /// que no reproduce nada. Por eso se agregó
    /// <see cref="IHostServices.GetOwningRoot"/> al contrato (SDK 1.5.0).
    ///
    /// <c>VaultRoot</c> queda de reserva para cuando no hay nota activa todavía.
    /// </summary>
    public static string? ActiveRoot(IHostServices host)
    {
        var active = host.ActiveFilePath;
        return (active is not null ? host.GetOwningRoot(active) : null) ?? host.VaultRoot;
    }

    /// <summary>
    /// El filtro sale de la MISMA lista que decide qué se reproduce. Si el usuario
    /// agrega .mkv en la ventana de Complementos, el diálogo se lo ofrece; si lo
    /// saca, deja de ofrecerlo. Una sola fuente de verdad.
    /// </summary>
    private string BuildFilter(MediaKind kind)
    {
        const string todos = "Todos los archivos (*.*)|*.*";

        var extensiones = _formats.ExtensionsOf(kind);
        if (extensiones.Count == 0) return todos;

        var patrones = string.Join(";", extensiones.Select(e => "*" + e));
        var rotulo   = kind == MediaKind.Video ? "Video" : "Audio";

        return $"{rotulo} ({patrones})|{patrones}|{todos}";
    }

    /// <summary>
    /// Arranca en <c>attachments/</c> si existe —es donde el host deja las imágenes
    /// pegadas, así que es donde el usuario ya guarda sus adjuntos— y si no, en la
    /// raíz del vault. Cadena vacía deja que Windows elija, que es lo correcto
    /// cuando no hay vault abierto.
    /// </summary>
    private static string StartingFolder(string? vaultRoot)
    {
        if (string.IsNullOrWhiteSpace(vaultRoot) || !Directory.Exists(vaultRoot)) return "";

        var adjuntos = Path.Combine(vaultRoot, "attachments");
        return Directory.Exists(adjuntos) ? adjuntos : vaultRoot;
    }

    // ─── formatos.json ───────────────────────────────────────────────────────

    /// <summary>
    /// Siembra el archivo la primera vez y lo lee. Mismo patrón que
    /// <c>pronunciaciones.json</c> del Lector: el usuario encuentra un archivo con
    /// contenido razonable, no uno vacío que no sabe cómo llenar.
    /// </summary>
    private static MediaFormats LoadFormats(IPluginContext context)
    {
        try
        {
            if (!context.Storage.Exists(MediaFormats.FileName))
            {
                _ = context.Storage.WriteTextAsync(
                    MediaFormats.FileName, MediaFormats.DefaultsAsJson());
                return MediaFormats.Default;
            }

            var json = Task.Run(() => context.Storage.ReadTextAsync(MediaFormats.FileName))
                           .GetAwaiter().GetResult();
            return MediaFormats.Parse(json, context.Log);
        }
        catch (Exception ex)
        {
            // Que no se pueda leer el sandbox NO puede tumbar el plugin: sin esto,
            // un archivo bloqueado dejaría la vista previa sin reproductores Y
            // marcaría el complemento en rojo.
            context.Log($"No se pudieron leer los formatos ({ex.Message}); se usan los de fábrica.");
            return MediaFormats.Default;
        }
    }

    private IReadOnlyList<PluginListEntry> LoadEntries() =>
        _formats.Entries
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new PluginListEntry(kv.Key, kv.Value))
                .ToList();

    /// <summary>
    /// Persiste y actualiza EN MEMORIA. El orden importa —primero el archivo,
    /// después el campo—: si escribir falla, la excepción sube al host, que la
    /// muestra junto a la lista y NO da el guardado por hecho; los formatos en
    /// memoria quedan como estaban. Al revés dejaría la pantalla diciendo
    /// "guardado" con el disco sin tocar.
    ///
    /// A diferencia del glosario del Dictado —donde los términos no entran en vigor
    /// hasta reiniciar el motor— acá el efecto es inmediato: se pide un re-render y
    /// el reproductor aparece en el acto.
    /// </summary>
    private void SaveEntries(IReadOnlyList<PluginListEntry> entries)
    {
        // Copia local: el campo se captura dentro de una lambda, donde el análisis de
        // nulabilidad no arrastra la comprobación de arriba.
        var context = _context;
        if (context is null) return;

        var normalized = new List<KeyValuePair<string, string>>(entries.Count);
        foreach (var e in entries)
        {
            var ext = MediaFormats.Normalize(e.Key);
            if (ext is not null) normalized.Add(new(ext, e.Value ?? ""));
        }

        var updated = MediaFormats.From(normalized);

        Task.Run(() => context.Storage.WriteTextAsync(MediaFormats.FileName, updated.ToJson()))
            .GetAwaiter().GetResult();

        _formats = updated;
        context.RequestPreviewRefresh();
    }

    /// <summary>
    /// El aviso bajo la lista. Mantiene las reglas PROPIAS del plugin dentro del
    /// plugin: el host no sabe —ni tiene por qué saber— qué códecs abre WebView2.
    /// </summary>
    private string? Describe(IReadOnlyList<PluginListEntry> entries)
    {
        var current = MediaFormats.From(
            entries.Select(e => new KeyValuePair<string, string>(e.Key, e.Value ?? "")));

        var parts = new List<string>
        {
            $"{current.Count} formatos · {current.ExtensionsOf(MediaKind.Video).Count} video · " +
            $"{current.ExtensionsOf(MediaKind.Audio).Count} audio"
        };

        if (current.IncompleteCount > 0)
            parts.Add($"{current.IncompleteCount} sin tipo (guardadas, pero no reproducen: " +
                      "escribí «video» o «audio»)");

        var unsupported = current.UnsupportedDeclared;
        if (unsupported.Count > 0)
            parts.Add($"⚠ {string.Join(", ", unsupported)}: el motor de la vista previa no abre " +
                      "esos contenedores. Van a mostrarse como un reproductor vacío. " +
                      "Convertí el archivo a .mp4 (H.264) o .webm.");

        return string.Join(" · ", parts);
    }

    // ─── Estilos ─────────────────────────────────────────────────────────────

    // El CSS del host trae `img { max-width:100% }` pero NADA para video ni audio:
    // sin esta regla un clip de 1920 px se desborda del ancho de la vista previa.
    private const string Css = """
        .mv-media {
            display: block;
            max-width: 100%;
            margin: 16px 0;
            border-radius: 6px;
        }
        video.mv-media {
            /* Fondo propio: mientras carga el primer fotograma, el hueco transparente
               deja ver el papel blanco y parpadea al empezar a reproducir. */
            background: #000;
            /* Sin esto, un video vertical de teléfono ocupa toda la pantalla. */
            max-height: 70vh;
        }
        audio.mv-media {
            width: 100%;
            border-radius: 0;
        }
        """;

    // ─── Continuidad de reproducción ─────────────────────────────────────────

    // El problema: la vista previa se actualiza con window.__mvSetBody, que hace
    // `#mv-content.innerHTML = …`. Eso DESTRUYE y recrea el <video> con cada tecla
    // que tocás — el video se reinicia en 0:00 y se pausa. Con imágenes nadie lo
    // nota (no tienen estado); con un video es inusable.
    //
    // La solución no necesita ni disco ni un canal hacia C#: la página NO se recarga
    // en ese camino, solo se reemplaza un innerHTML, así que una variable de `window`
    // sobrevive. Se fotografía el estado justo antes del reemplazo y se restaura
    // justo después.
    private const string KeepAliveScript = """
        (function () {
            var SEL   = 'video.mv-media, audio.mv-media';
            var state = window.__mvMediaState || (window.__mvMediaState = {});

            function each(fn) {
                var list = document.querySelectorAll(SEL);
                for (var i = 0; i < list.length; i++) fn(list[i]);
            }

            // La clave es el src: es estable entre re-renders y no exige tocar el HTML.
            function snapshot() {
                each(function (el) {
                    var key = el.getAttribute('src');
                    if (!key) return;
                    state[key] = {
                        t:      el.currentTime,
                        paused: el.paused,
                        volume: el.volume,
                        muted:  el.muted,
                        rate:   el.playbackRate
                    };
                });
            }

            function apply(el, s) {
                try { if (s.t > 0) el.currentTime = s.t; } catch (e) { /* medio sin seek */ }
                el.volume       = s.volume;
                el.muted        = s.muted;
                el.playbackRate = s.rate;
                if (!s.paused) {
                    var p = el.play();
                    // Chromium puede rechazar un play() sin gesto del usuario. No es un
                    // error que valga ensuciar la consola: el usuario ve el reproductor
                    // pausado en el segundo correcto y le da play.
                    if (p && p.catch) p.catch(function () {});
                }
            }

            function restore() {
                each(function (el) {
                    var key = el.getAttribute('src');
                    var s   = key && state[key];
                    if (!s) return;
                    // currentTime solo se puede fijar con los metadatos ya leídos.
                    if (el.readyState >= 1) { apply(el, s); return; }
                    el.addEventListener('loadedmetadata', function once() {
                        el.removeEventListener('loadedmetadata', once);
                        apply(el, s);
                    });
                });
            }

            document.addEventListener('DOMContentLoaded', function () {
                // El envoltorio se pone UNA sola vez, y acá y no antes: este script vive
                // al final del body, o sea que corre ANTES de que el host defina
                // __mvSetBody más abajo. Para cuando DOMContentLoaded dispara, ya existe.
                if (!window.__mvMediaHooked && typeof window.__mvSetBody === 'function') {
                    window.__mvMediaHooked = true;
                    var original = window.__mvSetBody;
                    window.__mvSetBody = function (html) {
                        snapshot();      // el DOM viejo sigue vivo: último momento útil
                        original(html);  // …y esto re-dispara DOMContentLoaded → restore()
                    };
                }
                restore();
            });
        })();
        """;
}

/// <summary>Envuelve la extensión de Markdig sin filtrar el tipo al contrato del SDK.</summary>
internal sealed class MediaContribution : IMarkdownContribution
{
    private readonly Func<MediaFormats> _formats;

    public MediaContribution(Func<MediaFormats> formats) => _formats = formats;

    public object CreateMarkdigExtension() => new MediaMarkdigExtension(_formats);
}
