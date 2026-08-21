using MarkdownVault.Models;
using MarkdownVault.PluginSdk;

namespace MarkdownVault.ViewModels;

/// <summary>
/// Destino de UNA escritura pedida por un comando de plugin. Se resuelve en CADA
/// escritura, no una sola vez al principio: una sesión de dictado arranca con su
/// pestaña activa y puede terminar con esa misma pestaña en segundo plano — o cerrada.
/// </summary>
internal enum PluginWriteRoute
{
    /// <summary>La pestaña fijada SIGUE siendo la activa de su panel → control vivo (AvalonEdit).</summary>
    LiveEditor,

    /// <summary>Sigue abierta pero en segundo plano → se escribe directo en su <see cref="OpenTab"/>.</summary>
    PinnedTabModel,

    /// <summary>Se cerró mientras la operación estaba en curso → degradar con aviso, sin escribir.</summary>
    TabClosed,

    /// <summary>No había ningún documento abierto al ejecutar el comando → degradar con aviso.</summary>
    NoDocument
}

/// <summary>
/// Puente entre <see cref="IEditorContext"/> (SDK) y el <see cref="EditorGroupViewModel"/>.
///
/// FIJA («pin») la pestaña activa en el instante en que el usuario EJECUTA el comando y manda
/// todas las escrituras de esa invocación a ESA pestaña, aunque el usuario se cambie de
/// pestaña o de panel mientras la operación está en curso.
///
/// El porqué, que no es teórico: hay UN solo control de edición por panel y cambiar de pestaña
/// le reemplaza el contenido a ese mismo control (<c>EditorView.OnActiveTabChanged</c> →
/// <c>SetEditorText(tab.Content)</c>). Antes de este tipo había UN adapter cacheado por panel,
/// compartido por todos los items de la barra, y las escrituras iban siempre "a la pestaña
/// activa AHORA". Un plugin que retiene el <see cref="IEditorContext"/> y escribe más tarde
/// —dictado en vivo, una frase por pausa; transcripción de archivo, minutos después— insertaba
/// en el documento que estuviera activo en ese momento. Sin error y sin aviso: corrupción
/// silenciosa de contenido.
///
/// Por eso se crea UNO POR INVOCACIÓN (ver <see cref="EditorGroupViewModel.CreatePluginEditorContext"/>)
/// y no uno cacheado por panel: el pin es justamente lo que distingue una invocación de otra.
///
/// Los plugins SINCRÓNICOS (Callouts, Highlight, CopyButton, Mermaid, Eisenhower) insertan
/// dentro del mismo clic: para ellos la pestaña fijada ES la activa, así que caen siempre en
/// <see cref="PluginWriteRoute.LiveEditor"/> y su comportamiento no cambia en absolutamente nada.
/// </summary>
internal sealed class PinnedEditorContext : IEditorContext
{
    private readonly EditorGroupViewModel _origin;
    private readonly OpenTab?             _pinnedTab;

    // El aviso de degradación se emite UNA sola vez por invocación: el dictado en vivo llama
    // a ReplaceSelection en cada frase y repetir el mensaje taparía la barra de estado.
    private bool _degradedWarned;

    internal PinnedEditorContext(EditorGroupViewModel origin, OpenTab? pinnedTab)
    {
        _origin    = origin;
        _pinnedTab = pinnedTab;
    }

    /// <summary>La pestaña fijada al ejecutar el comando (null si no había ninguna abierta).</summary>
    internal OpenTab? PinnedTab => _pinnedTab;

    // ─── Enrutado ────────────────────────────────────────────────────────────

    /// <summary>
    /// Decide a dónde va esta escritura. <paramref name="target"/> es el panel que TIENE la
    /// pestaña ahora mismo, que no siempre es el de origen: «Mover al otro panel» y salir del
    /// split MIGRAN la instancia de <see cref="OpenTab"/> de un grupo a otro
    /// (<c>MainViewModel.MoveTabToOtherGroup</c> / <c>ExitSplit</c>). Seguir a la pestaña en
    /// vez de al panel es lo que mantiene sana la promesa "el texto va al documento donde la
    /// acción empezó" con el editor dividido.
    /// </summary>
    internal PluginWriteRoute ResolveRoute(out EditorGroupViewModel target)
    {
        target = _origin;

        if (_pinnedTab is null)
        {
            // No había pestaña que fijar → no hay destino legítimo, punto. Antes, mientras el
            // panel siguiera vacío, esto devolvía LiveEditor y el texto iba al control sin
            // pestaña detrás: no lo guardaba nadie y la primera apertura de archivo lo pisaba.
            // Ese "camino de siempre" era justamente el agujero; ahora degrada con aviso.
            return PluginWriteRoute.NoDocument;
        }

        var owner = _origin.ResolveOwner(_pinnedTab);
        if (owner is null) return PluginWriteRoute.TabClosed;

        target = owner;
        return ReferenceEquals(owner.ActiveTab, _pinnedTab)
            ? PluginWriteRoute.LiveEditor
            : PluginWriteRoute.PinnedTabModel;
    }

    // ─── IEditorContext ──────────────────────────────────────────────────────

    /// <summary>
    /// Contenido de la pestaña FIJADA, no el del panel. Si el usuario ya se cambió de pestaña,
    /// <c>_origin.Content</c> sería el documento equivocado — es el mismo bug, en versión
    /// lectura (p. ej. «Leer documento» del plugin LectorDocumentos leyendo en voz alta el
    /// archivo que no es). Si la pestaña se cerró devuelve su último contenido conocido: leer
    /// no corrompe nada y evita que un plugin lector explote a mitad de camino.
    /// </summary>
    public string Content => _pinnedTab?.Content ?? _origin.Content;

    /// <summary>
    /// Una SELECCIÓN solo existe en el control vivo: es estado del AvalonEdit (offset + largo
    /// sobre el documento que está cargado en pantalla), no del modelo — <see cref="OpenTab"/>
    /// guarda caret y scroll, no selección. Una pestaña en segundo plano, por definición, no
    /// tiene ninguna. Por eso acá se devuelve VACÍO en vez de inventar una a partir del caret:
    /// los plugins ya tratan "sin selección" como un caso normal y bien definido (insertar en
    /// el cursor en vez de reemplazar), así que degradar por ese camino es seguro; devolver
    /// texto que el usuario no seleccionó llevaría a reemplazarlo sin que lo haya pedido.
    /// </summary>
    public string SelectedText =>
        ResolveRoute(out var target) == PluginWriteRoute.LiveEditor
            ? target.PluginGetSelectedText()
            : string.Empty;

    public void InsertAtCaret(string text)
    {
        switch (ResolveRoute(out var target))
        {
            case PluginWriteRoute.LiveEditor:     target.PluginInsertAtCaret(text);           break;
            case PluginWriteRoute.PinnedTabModel: InsertIntoPinnedTab(text, onFreshLine: true); break;
            default:                              WarnDegraded();                             break;
        }
    }

    public void ReplaceSelection(string text)
    {
        switch (ResolveRoute(out var target))
        {
            case PluginWriteRoute.LiveEditor:     target.PluginReplaceSelection(text);          break;
            // Sin selección que reemplazar (ver SelectedText) → inserción pura en el caret
            // guardado, que es lo mismo que hace la vista cuando no hay nada seleccionado.
            case PluginWriteRoute.PinnedTabModel: InsertIntoPinnedTab(text, onFreshLine: false); break;
            default:                              WarnDegraded();                               break;
        }
    }

    /// <summary>
    /// En el camino vivo, el comportamiento de siempre. En una pestaña en segundo plano no hay
    /// selección que envolver (ver <see cref="SelectedText"/>), así que degrada a INSERCIÓN de
    /// los marcadores en el caret guardado, dejando el caret ENTRE los dos — donde el usuario
    /// escribiría al volver. Degradar y no descartar: tirar el texto en silencio sería otra
    /// pérdida de contenido, justo lo que este tipo existe para evitar.
    /// </summary>
    public void WrapSelection(string before, string after)
    {
        switch (ResolveRoute(out var target))
        {
            case PluginWriteRoute.LiveEditor:     target.PluginWrapSelection(before, after); break;
            case PluginWriteRoute.PinnedTabModel: WrapIntoPinnedTab(before, after);          break;
            default:                              WarnDegraded();                            break;
        }
    }

    // ─── Escritura directa en el modelo de la pestaña fijada ─────────────────

    /// <summary>
    /// Inserta en la pestaña fijada SIN tocar ningún control. Replica lo que hace la vista en
    /// el camino vivo (<c>EditorView.Vm_SnippetRequested</c>): salto de línea previo si el
    /// caret no está al principio de una línea.
    ///
    /// Hace TRES cosas y las tres son obligatorias:
    ///   1. inserta en el <see cref="OpenTab.CaretOffset"/> guardado — no al final del texto;
    ///   2. AVANZA ese caret, para que varias frases seguidas de dictado se encadenen en vez de
    ///      pisarse todas en el mismo offset;
    ///   3. marca la pestaña como sucia igual que una edición manual — si no, el usuario cierra
    ///      sin guardar y pierde el texto sin que nadie le pregunte nada (<c>CloseTab</c> solo
    ///      pregunta cuando <see cref="OpenTab.IsDirty"/>).
    /// </summary>
    private void InsertIntoPinnedTab(string text, bool onFreshLine)
    {
        var tab     = _pinnedTab!;
        var content = tab.Content;
        var offset  = ClampCaret(tab, content);

        // El caret está "al principio de línea" si es 0 o viene justo después de un \n. Con
        // CRLF el \n es el último carácter del delimitador, así que la prueba vale igual.
        var prefix = onFreshLine && offset > 0 && content[offset - 1] != '\n' ? "\n" : "";
        var chunk  = prefix + text;

        Commit(tab, content.Insert(offset, chunk), offset + chunk.Length);
    }

    private void WrapIntoPinnedTab(string before, string after)
    {
        var tab     = _pinnedTab!;
        var content = tab.Content;
        var offset  = ClampCaret(tab, content);

        // Prefijo de línea ("# ", "- "): la vista lo mete al PRINCIPIO de la línea del caret,
        // no en el caret. Se replica para que el texto resultante sea el mismo por los dos
        // caminos — si no, un encabezado terminaría partiendo la línea al medio.
        if (string.IsNullOrEmpty(after) && !before.Contains('\n'))
        {
            var lineStart = offset == 0 ? 0 : content.LastIndexOf('\n', offset - 1) + 1;
            Commit(tab, content.Insert(lineStart, before), lineStart + before.Length);
            return;
        }

        Commit(tab, content.Insert(offset, before + after), offset + before.Length);
    }

    /// <summary>
    /// El caret guardado puede haber quedado más allá del final del texto (recarga desde disco
    /// de un archivo que se acortó afuera, ver <c>ReloadTabFromDiskAsync</c>). Acotarlo evita
    /// la excepción y deja la inserción al final, que es el destino menos sorprendente.
    /// </summary>
    private static int ClampCaret(OpenTab tab, string content) =>
        Math.Clamp(tab.CaretOffset, 0, content.Length);

    private static void Commit(OpenTab tab, string content, int caret)
    {
        tab.Content     = content;
        tab.CaretOffset = caret;
        tab.IsDirty     = true;
    }

    // ─── Degradación ─────────────────────────────────────────────────────────

    /// <summary>
    /// Degradación EXPLÍCITA: nunca una excepción y NUNCA escribir en otro documento. Se avisa
    /// una sola vez por invocación (el dictado escribe una vez por frase, serían decenas de
    /// mensajes) y las escrituras siguientes se descartan en silencio.
    /// </summary>
    private void WarnDegraded()
    {
        if (_degradedWarned) return;
        _degradedWarned = true;

        _origin.StatusSink?.Invoke(_pinnedTab is null
            ? "No había ningún documento abierto cuando empezó la operación: el texto no se insertó."
            : $"«{_pinnedTab.FileName}» se cerró: el texto ya no se puede insertar ahí. " +
              "Detené la operación y volvé a empezarla en el documento que quieras.");
    }
}
