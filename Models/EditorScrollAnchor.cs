namespace MarkdownVault.Models;

/// <summary>
/// Posición de lectura del editor expresada en unidades del DOCUMENTO: la primera línea
/// visible arriba del viewport, más cuántos píxeles de ESA línea ya quedaron por encima del
/// borde (precisión sub-línea, para un párrafo que ocupa media pantalla).
///
/// POR QUÉ NO ES UN OFFSET EN PÍXELES — y por qué no hay que "simplificarlo" de vuelta a uno:
/// con <c>WordWrap="True"</c> un píxel no identifica nada estable. El mismo párrafo ocupa
/// 3 líneas visuales con el panel ancho y 9 con el panel angosto, así que un offset guardado
/// deja de apuntar al mismo texto apenas el usuario mueve el splitter o cambia el tamaño de
/// fuente. La línea del documento, en cambio, es la misma siempre.
/// </summary>
/// <param name="Line">Línea del documento (1-based) que estaba arriba de todo.</param>
/// <param name="LineDelta">Píxeles de esa línea que quedaron por encima del borde superior.</param>
public readonly record struct EditorScrollAnchor(int Line, double LineDelta)
{
    /// <summary>Arriba de todo: el estado de una pestaña recién abierta.</summary>
    public static readonly EditorScrollAnchor Top = new(1, 0);

    /// <summary>
    /// Arma el ancla a partir de lo que reporta el editor: la primera línea visual visible
    /// (<paramref name="topLineNumber"/>, con su tope en <paramref name="lineVisualTop"/>) y el
    /// scroll actual. Ambos valores se normalizan — una línea nunca es menor que 1 y el delta
    /// nunca es negativo — para que un ancla mal medida no pueda pedir un scroll imposible.
    /// </summary>
    public static EditorScrollAnchor Capture(int topLineNumber, double lineVisualTop, double verticalOffset) =>
        new(Math.Max(1, topLineNumber), Math.Max(0, verticalOffset - lineVisualTop));

    /// <summary>
    /// Offset vertical que hoy representa este ancla, dado el tope y el alto de su línea en el
    /// árbol de alturas ACTUAL. Se recalcula en cada pase de layout a propósito: los dos valores
    /// se corrigen solos a medida que AvalonEdit mide de verdad las líneas que va renderizando.
    ///
    /// EL RECORTE DEL DELTA NO ES DEFENSIVO, es lo que hace que el bucle pueda converger. El
    /// delta solo significa algo DENTRO de su línea, y mientras esa línea todavía no se midió
    /// vale la altura por defecto —una sola línea de texto—. Un delta guardado sobre un párrafo
    /// ya envuelto (medido: 103,7 px) se pasaría de largo hacia las líneas siguientes, y ahí el
    /// asentamiento queda trabado: el destino es coherente consigo mismo y aun así está
    /// mostrando otra línea, así que no hay nada que corregir y nunca llega. Recortado, la
    /// primera pasada aterriza DENTRO de la línea correcta, eso obliga a medirla, y recién en la
    /// pasada siguiente el delta completo vuelve a ser aplicable.
    /// </summary>
    public double DesiredOffset(double lineVisualTop, double lineHeight)
    {
        var usable = Math.Clamp(LineDelta, 0, Math.Max(0, lineHeight - 1));
        return Math.Max(0, lineVisualTop + usable);
    }

    /// <summary>True cuando el ancla es "arriba de todo" y no hace falta restaurar nada.</summary>
    public bool IsTop => Line <= 1 && LineDelta <= 0;
}
