namespace MarkdownVault.Services;

/// <summary>Qué hacer en un tick del bucle de asentamiento del scroll.</summary>
public enum ScrollSettleAction
{
    /// <summary>Llegamos: el viewport ya está donde lo queríamos.</summary>
    Done,

    /// <summary>Todavía no: volver a pedir el offset recalculado.</summary>
    Apply,

    /// <summary>Se agotaron los intentos. Se corta igual — nunca se itera sin techo.</summary>
    GiveUp,
}

/// <summary>
/// Bucle de asentamiento del scroll, en lógica PURA (sin WPF ni AvalonEdit, por eso se testea
/// headless igual que <see cref="TextSearch"/>).
///
/// EL MECANISMO, que es lo que hay que entender antes de tocar esto: AvalonEdit mantiene un
/// árbol de alturas donde cada línea que TODAVÍA NO SE RENDERIZÓ vale la altura por defecto de
/// UNA sola línea. Con <c>WordWrap</c>, un párrafo de Markdown es una línea lógica que se parte
/// en decenas de líneas visuales, así que apenas se reemplaza el documento (cambio de pestaña)
/// el árbol subestima groseramente la altura real: <c>ExtentHeight</c> queda corto y el
/// ScrollViewer RECORTA en silencio cualquier offset mayor a <c>ExtentHeight - ViewportHeight</c>
/// (medido: se pidió 4857 px y el viewport quedó en 1317).
///
/// La salida no es adivinar el píxel: es pedir el destino, dejar que el scroll obligue a
/// AvalonEdit a MEDIR de verdad las líneas que quedan a la vista, y volver a preguntar. Cada
/// pasada el árbol es menos mentiroso y el destino se corrige solo. Converge en pocas
/// iteraciones (medido: 6 para volver a 4857 px exactos).
///
/// Tres salidas duras y ninguna más: llegó, se acabaron los intentos, o el que maneja
/// (<c>EditorScrollRestorer</c>) corta porque el usuario tocó algo. Sin ese techo, un destino
/// inalcanzable —una línea dentro de la última pantalla del documento— iteraría para siempre.
/// </summary>
public sealed class ScrollSettle
{
    /// <summary>
    /// Doce alcanza y sobra: lo medido converge en seis. Es un techo de seguridad, no un
    /// presupuesto — cada intento es un pase de layout, no un frame perdido.
    /// </summary>
    public const int DefaultMaxAttempts = 12;

    /// <summary>Medio píxel: por debajo de eso no hay diferencia que un ojo pueda ver.</summary>
    public const double DefaultTolerance = 0.5;

    private readonly int    _maxAttempts;
    private readonly double _tolerance;
    private          int    _attempts;

    public ScrollSettle(int maxAttempts = DefaultMaxAttempts, double tolerance = DefaultTolerance)
    {
        _maxAttempts = Math.Max(1, maxAttempts);
        _tolerance   = Math.Max(0, tolerance);
    }

    /// <summary>Intentos ya consumidos. Solo para diagnóstico y para los tests.</summary>
    public int Attempts => _attempts;

    /// <summary>
    /// Decide el tick.
    ///
    /// <paramref name="arrived"/> se mide sobre lo que el editor REALMENTE está mostrando —la
    /// línea que quedó arriba de todo—, NO comparando píxeles. Esa distinción no es cosmética:
    /// medida contra el píxel, la restauración cortaba UN tick antes de tiempo. El destino en
    /// píxeles y el pintado se calculan los dos contra el árbol de alturas, pero el propio
    /// pintado ACTUALIZA ese árbol en el mismo pase, así que "el offset pedido es el offset
    /// actual" puede ser cierto y aun así estar mostrando otra línea. Medido: en una nota de 43
    /// líneas se guardaba la 29 y se aterrizaba en la 33.
    ///
    /// <paramref name="currentOffset"/> y <paramref name="desiredOffset"/> entran solo para el
    /// corte de seguridad: si el destino ya ES donde estamos y ni así llegamos, volver a pedirlo
    /// no mueve nada ni dispara otro pase de layout — el bucle se quedaría suscripto sin avanzar.
    /// </summary>
    public ScrollSettleAction Next(bool arrived, double currentOffset, double desiredOffset)
    {
        if (arrived)
            return ScrollSettleAction.Done;

        if (_attempts >= _maxAttempts)
            return ScrollSettleAction.GiveUp;

        if (_attempts > 0 && Math.Abs(currentOffset - desiredOffset) <= _tolerance)
            return ScrollSettleAction.GiveUp;

        _attempts++;
        return ScrollSettleAction.Apply;
    }
}

/// <summary>
/// Cuentas puras de destino de scroll. Separadas del control para poder verificarlas sin un
/// <c>TextEditor</c> vivo.
/// </summary>
public static class EditorScrollPolicy
{
    /// <summary>
    /// Fracción del viewport a la que se deja la línea revelada cuando hay que moverse. Es 0,5
    /// —centrada— porque ese era el comportamiento del <c>ScrollTo(line, column)</c> anterior
    /// (AvalonEdit revela con <c>VisualYPosition.LineMiddle</c>), y este cambio unificó el
    /// MECANISMO de scroll, no la sensación de uso: Buscar y F3 tienen que seguir sintiéndose
    /// igual que siempre. Un tercio desde arriba se probó y se descartó: deja mejor contexto
    /// para leer, pero cambiarle el tacto a la búsqueda no era parte de este arreglo.
    /// </summary>
    public const double RevealFraction = 0.5;

    /// <summary>
    /// Offset al que hay que ir para REVELAR una línea (salto a un ancla, coincidencia de
    /// Buscar). Si la línea ya entra entera en pantalla devuelve el offset actual — o sea, no
    /// mover nada: sin esa rama, repetir F3 dentro de la misma pantalla haría saltar la vista
    /// en cada coincidencia.
    /// </summary>
    public static double RevealOffset(
        double lineTop, double lineHeight, double currentOffset, double viewportHeight)
    {
        if (viewportHeight <= 0) return currentOffset;

        bool alreadyVisible = lineTop >= currentOffset
                              && lineTop + lineHeight <= currentOffset + viewportHeight;

        return alreadyVisible
            ? currentOffset
            : Math.Max(0, lineTop - viewportHeight * RevealFraction);
    }
}
