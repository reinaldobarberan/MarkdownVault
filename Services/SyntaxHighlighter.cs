using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;

namespace MarkdownVault.Services;

/// <summary>
/// Colorea líneas reutilizando las definiciones de sintaxis de AvalonEdit (ya presente en el
/// proyecto). Resalta el documento COMPLETO una vez, de modo que preserva el contexto
/// multi-línea (comentarios de bloque, strings sin cerrar), y expone el color de primer plano
/// por carácter de cada línea. El renderer del diff compone ese color con el resaltado de
/// diferencias carácter por carácter, sin que los <c>&lt;span&gt;</c> de uno rompan al otro.
///
/// Degradación segura: si no hay definición para la extensión (o algo falla), queda
/// deshabilitado y las líneas se muestran sin color (comportamiento previo).
/// </summary>
public sealed class SyntaxHighlighter
{
    private readonly TextDocument?       _doc;
    private readonly DocumentHighlighter? _hl;

    public SyntaxHighlighter(string text, string? fileExtension)
    {
        try
        {
            var def = string.IsNullOrEmpty(fileExtension)
                ? null
                : HighlightingManager.Instance.GetDefinitionByExtension(fileExtension);
            if (def is null) return;

            _doc = new TextDocument(text);
            _hl  = new DocumentHighlighter(_doc, def);
        }
        catch
        {
            _doc = null;
            _hl  = null;
        }
    }

    public bool Enabled => _hl is not null;

    /// <summary>
    /// Color hex (<c>#rrggbb</c>) de primer plano por carácter de la línea 1-based indicada, o
    /// <c>null</c> si el resaltado no está disponible. Las posiciones sin color explícito quedan
    /// en <c>null</c> dentro del arreglo (se pintan con el color por defecto del tema).
    /// </summary>
    public string?[]? ColorsForLine(int lineNumber)
    {
        if (_hl is null || _doc is null) return null;
        if (lineNumber < 1 || lineNumber > _doc.LineCount) return null;

        try
        {
            var line   = _doc.GetLineByNumber(lineNumber);
            var colors = new string?[line.Length];
            var hlLine = _hl.HighlightLine(lineNumber);

            foreach (var sec in hlLine.Sections)
            {
                var hex = ToHex(sec.Color?.Foreground?.GetColor(null));
                if (hex is null) continue;

                int local = sec.Offset - line.Offset;
                for (int k = 0; k < sec.Length; k++)
                {
                    int idx = local + k;
                    if (idx >= 0 && idx < colors.Length) colors[idx] = hex;
                }
            }
            return colors;
        }
        catch
        {
            return null;
        }
    }

    private static string? ToHex(System.Windows.Media.Color? c) =>
        c is { } v ? $"#{v.R:x2}{v.G:x2}{v.B:x2}" : null;
}
