using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using MarkdownVault.Models;
using MarkdownVault.Services;

namespace MarkdownVault.Views;

/// <summary>
/// Lleva el scroll vertical de un <see cref="TextEditor"/> a una posición del DOCUMENTO y se
/// queda insistiendo hasta que llega, mientras el árbol de alturas de AvalonEdit termina de
/// enterarse de cuánto miden las líneas de verdad.
///
/// POR QUÉ NO ALCANZA CON GUARDAR Y RESTAURAR EL PÍXEL (el bug que esto cierra, y la razón por
/// la que no hay que "simplificar" esta clase de vuelta a un <c>ScrollToVerticalOffset</c>
/// suelto): AvalonEdit le asigna la altura POR DEFECTO —la de UNA línea— a toda línea que
/// todavía no renderizó. Con <c>WordWrap="True"</c> un párrafo de Markdown es UNA línea lógica
/// partida en decenas de líneas visuales, así que al reemplazar el documento en un cambio de
/// pestaña el árbol arranca subestimando la altura total por un factor enorme. El ScrollViewer
/// recorta contra ese <c>ExtentHeight</c> falso, en silencio y sin devolver error: se le pide
/// 4857 px y el viewport queda en 1317. En una nota corta el recorte colapsa a ~0 y la pestaña
/// aparece directamente arriba de todo. Con <c>WordWrap="False"</c> el mismo código restaura
/// exacto — esa es la prueba del mecanismo.
///
/// La unidad correcta entonces es la LÍNEA (ver <see cref="EditorScrollAnchor"/>), y el destino
/// en píxeles se recalcula en CADA pase de layout contra el árbol de ese momento. Es
/// autoconsistente —el pintado usa el mismo árbol— y se corrige solo: cada scroll obliga a
/// medir las líneas que quedan a la vista, el árbol crece, el destino sube, se vuelve a pedir.
///
/// Invariantes que este control tiene que cumplir sí o sí:
/// <list type="bullet">
/// <item>La desuscripción es DETERMINISTA: <see cref="Cancel"/> es el único punto de salida y
/// todos los caminos pasan por ahí (llegó, se agotó, el usuario tocó algo, otra restauración
/// nueva).</item>
/// <item>Manda el usuario: rueda, tecla o clic durante el asentamiento lo cortan. Pelearle a
/// alguien que está scrolleando es mucho peor que quedar unos píxeles corto.</item>
/// <item>Un pedido nuevo cancela al anterior, así que nunca hay dos asentamientos vivos
/// peleando por el mismo viewport.</item>
/// </list>
/// </summary>
internal sealed class EditorScrollRestorer
{
    private readonly TextEditor _editor;

    private ScrollSettle?      _settle;
    private EditorScrollAnchor _anchor = EditorScrollAnchor.Top;
    private int                _revealLine;   // 0 = modo ancla; >0 = modo revelar línea
    private bool               _running;

    public EditorScrollRestorer(TextEditor editor) => _editor = editor;

    /// <summary>True mientras hay un asentamiento en vuelo.</summary>
    public bool IsSettling => _running;

    /// <summary>
    /// Posición de lectura actual, o <c>null</c> cuando no se puede medir —el panel nunca se
    /// dibujó, o está colapsado por el modo de vista—. En ese caso el llamador NO debe pisar lo
    /// que ya tenía guardado: devolver "arriba de todo" ahí sería exactamente perder la
    /// posición que esta clase existe para no perder.
    /// </summary>
    public EditorScrollAnchor? Capture()
    {
        // Con un asentamiento de ANCLA en vuelo, lo que se ve es un paso INTERMEDIO de la
        // convergencia, no donde estaba leyendo el usuario. Se devuelve el destino que
        // estábamos persiguiendo: cambiar de pestaña a mitad del restore no debe degradar el
        // punto guardado.
        if (_running && _revealLine == 0) return _anchor;

        try
        {
            var view = _editor.TextArea.TextView;
            if (!view.VisualLinesValid) return null;

            var lines = view.VisualLines;
            if (lines.Count == 0) return null;

            // VisualLines viene ordenada de arriba hacia abajo desde el tope del viewport, así
            // que [0] ES la primera línea visible.
            var first = lines[0];
            return EditorScrollAnchor.Capture(
                first.FirstDocumentLine.LineNumber, first.VisualTop, view.VerticalOffset);
        }
        catch
        {
            // VisualLinesInvalidException y compañía: no se pudo medir, no se pisa nada.
            return null;
        }
    }

    /// <summary>Vuelve a la posición de lectura guardada (cambio de pestaña, recarga de disco).</summary>
    public void Restore(EditorScrollAnchor anchor)
    {
        Cancel();
        _anchor     = anchor;
        _revealLine = 0;
        Begin();
    }

    /// <summary>
    /// Revela una línea del documento (salto a un ancla, coincidencia de Buscar). Comparte
    /// mecanismo con <see cref="Restore"/> a propósito: <c>ScrollTo(line, column)</c> montaba
    /// sobre el MISMO árbol de alturas mentiroso, así que un salto a un ancla en el fondo de una
    /// nota larga también quedaba corto.
    /// </summary>
    public void RevealLine(int line)
    {
        Cancel();
        _revealLine = Math.Max(1, line);
        Begin();
    }

    /// <summary>Único punto de salida: desengancha TODO lo que <see cref="Begin"/> enganchó.</summary>
    public void Cancel()
    {
        if (!_running) return;
        _running = false;

        _editor.LayoutUpdated     -= OnLayoutUpdated;
        _editor.PreviewMouseWheel -= OnUserTookOver;
        _editor.PreviewMouseDown  -= OnUserTookOver;
        _editor.PreviewKeyDown    -= OnUserTookOver;
    }

    private void Begin()
    {
        _settle  = new ScrollSettle();
        _running = true;

        _editor.LayoutUpdated     += OnLayoutUpdated;
        _editor.PreviewMouseWheel += OnUserTookOver;
        _editor.PreviewMouseDown  += OnUserTookOver;
        _editor.PreviewKeyDown    += OnUserTookOver;

        // Primer intento YA, sin esperar el próximo pase de layout: así la primera pintada
        // después del cambio de pestaña ya sale cerca y no se ve el salto desde arriba.
        Tick();
    }

    private void OnUserTookOver(object sender, InputEventArgs e) => Cancel();

    private void OnLayoutUpdated(object? sender, EventArgs e) => Tick();

    private void Tick()
    {
        if (!_running || _settle is null) return;

        bool   arrived;
        double desired;
        try
        {
            if (!TryDesiredOffset(out desired)) return;   // todavía no hay nada medible
            arrived = Arrived(desired);
        }
        catch
        {
            Cancel();
            return;
        }

        if (_settle.Next(arrived, _editor.VerticalOffset, desired) == ScrollSettleAction.Apply)
            _editor.ScrollToVerticalOffset(desired);
        else
            Cancel();   // Done o GiveUp: en los dos casos se deja de insistir y se desengancha
    }

    /// <summary>
    /// ¿Ya estamos donde queríamos? Se pregunta sobre lo que el editor está MOSTRANDO —la
    /// primera línea visual visible—, nunca comparando el offset pedido con el actual: el
    /// pintado actualiza el árbol de alturas mientras pinta, así que los dos píxeles pueden
    /// coincidir y estar mostrando otra línea.
    /// </summary>
    private bool Arrived(double desired)
    {
        var doc  = _editor.Document;
        var view = _editor.TextArea.TextView;
        if (doc is null || doc.LineCount == 0) return false;
        if (!view.VisualLinesValid) return false;

        var lines = view.VisualLines;
        if (lines.Count == 0) return false;

        if (_revealLine > 0)
        {
            var target = Math.Clamp(_revealLine, 1, doc.LineCount);
            return lines.Any(v => v.FirstDocumentLine.LineNumber <= target
                                  && target <= v.LastDocumentLine.LineNumber);
        }

        // Las DOS condiciones, y en este orden: la línea correcta arriba de todo (lo que el
        // usuario ve) y además el offset exacto que pide el ancla recortada (la precisión
        // sub-línea). Solo la segunda es la que fallaba antes por sí sola.
        var wanted = Math.Clamp(_anchor.Line, 1, doc.LineCount);
        if (lines[0].FirstDocumentLine.LineNumber != wanted) return false;

        return Math.Abs(_editor.VerticalOffset - desired) <= ScrollSettle.DefaultTolerance;
    }

    /// <summary>
    /// Traduce el destino a píxeles contra el árbol de alturas de ESTE momento. Devuelve
    /// <c>false</c> cuando todavía no hay documento que medir (el tick se saltea sin gastar
    /// intento).
    /// </summary>
    private bool TryDesiredOffset(out double offset)
    {
        offset = 0;

        var doc  = _editor.Document;
        var view = _editor.TextArea.TextView;
        if (doc is null || doc.LineCount == 0) return false;

        if (_revealLine == 0)
        {
            var line = Math.Clamp(_anchor.Line, 1, doc.LineCount);
            offset = _anchor.DesiredOffset(
                view.GetVisualTopByDocumentLine(line), LineHeight(view, doc.LineCount, line));
            return true;
        }

        var target  = Math.Clamp(_revealLine, 1, doc.LineCount);
        var lineTop = view.GetVisualTopByDocumentLine(target);

        offset = EditorScrollPolicy.RevealOffset(
            lineTop, LineHeight(view, doc.LineCount, target),
            _editor.VerticalOffset, _editor.ViewportHeight);
        return true;
    }

    /// <summary>
    /// Alto de una línea del documento según el árbol de alturas de ESTE momento: la distancia
    /// hasta el tope de la siguiente. La última línea no tiene siguiente, así que cae al mínimo
    /// de 1 px — suficiente para que las cuentas que la usan sigan siendo válidas.
    /// </summary>
    private static double LineHeight(
        ICSharpCode.AvalonEdit.Rendering.TextView view, int lineCount, int line) =>
        line < lineCount
            ? Math.Max(1, view.GetVisualTopByDocumentLine(line + 1) - view.GetVisualTopByDocumentLine(line))
            : 1;
}
