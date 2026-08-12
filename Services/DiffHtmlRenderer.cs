using System.Text;

namespace MarkdownVault.Services;

/// <summary>
/// Convierte las filas de <see cref="DiffService"/> en una página HTML lado-a-lado
/// estilo Beyond Compare: dos columnas alineadas, resaltado por tipo de línea y
/// resaltado intra-línea de los tramos que realmente cambian. La alineación y el
/// scroll sincronizado salen "gratis" porque cada fila coloca izquierda y derecha en
/// un mismo contenedor flex dentro de un único scroll vertical.
///
/// Cada fila con diferencia lleva además dos botones (◀ ▶) en la columna central: al
/// pulsarlos, la página hace <c>postMessage</c> hacia el host (<c>window.chrome.webview</c>)
/// con el índice de fila y el sentido, para copiar esa línea al otro archivo.
/// </summary>
public static class DiffHtmlRenderer
{
    public static string Render(
        IReadOnlyList<DiffRow> rows, string leftTitle, string rightTitle, bool isDark,
        SyntaxHighlighter? leftSyntax = null, SyntaxHighlighter? rightSyntax = null)
    {
        var t = isDark ? Dark : Light;
        var body = new StringBuilder();

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];

            // Marca de cambio intra-línea (solo filas modificadas); en agregadas/borradas el
            // fondo de la celda ya comunica el cambio, así que no se marcan caracteres.
            bool[]? leftChanged = null, rightChanged = null;
            if (row.Kind == DiffLineKind.Modified)
            {
                leftChanged  = ChangedFlags(row.LeftSegments,  row.LeftText.Length);
                rightChanged = ChangedFlags(row.RightSegments, row.RightText.Length);
            }

            // Color de sintaxis por carácter (null si no hay resaltado para el lenguaje).
            var leftColors  = row.LeftLineNumber  is int ll ? leftSyntax?.ColorsForLine(ll)  : null;
            var rightColors = row.RightLineNumber is int rl ? rightSyntax?.ColorsForLine(rl) : null;

            var leftHtml  = RenderCell(row.LeftText,  leftColors,  leftChanged);
            var rightHtml = RenderCell(row.RightText, rightColors, rightChanged);

            body.Append("<div class=\"row ").Append(KindClass(row.Kind)).Append("\">")
                .Append("<div class=\"ln\">").Append(Num(row.LeftLineNumber)).Append("</div>")
                .Append("<div class=\"cell left\">").Append(leftHtml).Append("</div>")
                .Append("<div class=\"gutter\">").Append(Buttons(row.Kind, i, IsMultiRowBlockStart(rows, i))).Append("</div>")
                .Append("<div class=\"ln\">").Append(Num(row.RightLineNumber)).Append("</div>")
                .Append("<div class=\"cell right\">").Append(rightHtml).Append("</div>")
                .Append("</div>");
        }

        return $$"""
<!DOCTYPE html>
<html><head><meta charset="utf-8">
<style>
  * { box-sizing: border-box; }
  html, body { margin: 0; height: 100%; }
  body {
    font-family: Consolas, "Cascadia Code", "Courier New", monospace;
    font-size: 13px; line-height: 1.5;
    background: {{t.Bg}}; color: {{t.Fg}};
  }
  .head {
    display: flex; position: sticky; top: 0; z-index: 2;
    background: {{t.HeadBg}}; border-bottom: 1px solid {{t.Border}};
    font-family: "Segoe UI", sans-serif; font-size: 12px; font-weight: 600;
  }
  .head .h { flex: 1 1 0; padding: 6px 12px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .head .h + .h { border-left: 1px solid {{t.Border}}; }
  .row { display: flex; align-items: stretch; }
  .ln {
    flex: 0 0 46px; text-align: right; padding: 0 8px;
    color: {{t.LineNo}}; background: {{t.HeadBg}};
    user-select: none; border-right: 1px solid {{t.Border}};
  }
  .cell { flex: 1 1 0; padding: 0 10px; white-space: pre-wrap; word-break: break-word; overflow-wrap: anywhere; }
  .gutter {
    flex: 0 0 52px; display: flex; flex-direction: column;
    align-items: center; justify-content: center; gap: 2px;
    background: {{t.HeadBg}}; border-left: 1px solid {{t.Border}};
    border-right: 1px solid {{t.Border}}; user-select: none;
  }
  .brow { display: flex; gap: 2px; }
  .mbtn {
    font-family: "Segoe UI Symbol", sans-serif; font-size: 11px; line-height: 1;
    width: 20px; height: 18px; padding: 0; cursor: pointer;
    border: 1px solid {{t.Border}}; border-radius: 3px;
    background: {{t.BtnBg}}; color: {{t.Fg}};
  }
  .mbtn.blk { font-size: 9px; font-weight: bold; }
  .mbtn:hover { background: {{t.BtnHover}}; }
  /* Fila con contenido en ambos lados pero distinto */
  .row.mod .cell.left  { background: {{t.ModBg}}; }
  .row.mod .cell.right { background: {{t.ModBg}}; }
  /* Fila agregada: solo existe a la derecha */
  .row.add .cell.right { background: {{t.AddBg}}; }
  .row.add .cell.left  { background: {{t.FillerBg}}; }
  /* Fila borrada: solo existe a la izquierda */
  .row.del .cell.left  { background: {{t.DelBg}}; }
  .row.del .cell.right { background: {{t.FillerBg}}; }
  /* Tramos intra-línea que realmente cambiaron */
  .row.del .cell.left  .chg { background: {{t.DelInline}}; }
  .row.add .cell.right .chg { background: {{t.AddInline}}; }
  .row.mod .cell.left  .chg { background: {{t.DelInline}}; }
  .row.mod .cell.right .chg { background: {{t.AddInline}}; }
</style></head>
<body>
  <div class="head">
    <div class="ln" style="border-right:none;">&nbsp;</div>
    <div class="h">{{Escape(leftTitle)}}</div>
    <div class="gutter">&nbsp;</div>
    <div class="ln" style="border-right:none;">&nbsp;</div>
    <div class="h">{{Escape(rightTitle)}}</div>
  </div>
  <div class="rows">{{body}}</div>
  <script>
    function mv(row, dir, block) {
      if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage(JSON.stringify({ row: row, dir: dir, block: block === true }));
      }
    }
  </script>
</body></html>
""";
    }

    /// <summary>
    /// Botones de copia para la columna central. Vacío en filas sin diferencia. En la primera
    /// fila de un bloque de varias líneas agrega además los botones de bloque (◀◀/▶▶).
    /// </summary>
    private static string Buttons(DiffLineKind kind, int rowIndex, bool showBlock)
    {
        if (kind == DiffLineKind.Unchanged) return "&nbsp;";

        var line =
            "<div class=\"brow\">" +
            $"<button class=\"mbtn\" title=\"Copiar esta línea al izquierdo\" onclick=\"mv({rowIndex},'left')\">◀</button>" +
            $"<button class=\"mbtn\" title=\"Copiar esta línea al derecho\" onclick=\"mv({rowIndex},'right')\">▶</button>" +
            "</div>";

        if (!showBlock) return line;

        var block =
            "<div class=\"brow\">" +
            $"<button class=\"mbtn blk\" title=\"Copiar el bloque entero al izquierdo\" onclick=\"mv({rowIndex},'left',true)\">◀◀</button>" +
            $"<button class=\"mbtn blk\" title=\"Copiar el bloque entero al derecho\" onclick=\"mv({rowIndex},'right',true)\">▶▶</button>" +
            "</div>";

        return line + block;
    }

    /// <summary>True si la fila inicia un bloque de diferencias contiguas de más de una línea.</summary>
    private static bool IsMultiRowBlockStart(IReadOnlyList<DiffRow> rows, int i)
    {
        if (rows[i].Kind == DiffLineKind.Unchanged) return false;
        if (i > 0 && rows[i - 1].Kind != DiffLineKind.Unchanged) return false;   // no es el inicio

        int len = 0;
        for (int j = i; j < rows.Count && rows[j].Kind != DiffLineKind.Unchanged; j++) len++;
        return len > 1;
    }

    /// <summary>
    /// Arma el HTML de una celda fusionando dos segmentaciones independientes: el color de
    /// sintaxis por carácter (<paramref name="colors"/>) y la marca de cambio intra-línea
    /// (<paramref name="changed"/>). Recorre el texto agrupando caracteres contiguos que
    /// comparten (color, cambiado) en un único <c>&lt;span&gt;</c>, de modo que ninguna de las
    /// dos capas rompe a la otra. Ambos arreglos son opcionales/parciales; los índices sin dato
    /// se tratan como "sin color" / "sin cambio".
    /// </summary>
    private static string RenderCell(string text, string?[]? colors, bool[]? changed)
    {
        if (text.Length == 0) return "";

        var sb = new StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            string? color = ColorAt(colors, i);
            bool    chg   = ChangedAt(changed, i);

            int j = i + 1;
            while (j < text.Length && ColorAt(colors, j) == color && ChangedAt(changed, j) == chg)
                j++;

            var slice = Escape(text.Substring(i, j - i));
            if (color is null && !chg)
            {
                sb.Append(slice);
            }
            else
            {
                sb.Append("<span");
                if (chg)             sb.Append(" class=\"chg\"");
                if (color is not null) sb.Append(" style=\"color:").Append(color).Append('"');
                sb.Append('>').Append(slice).Append("</span>");
            }
            i = j;
        }
        return sb.ToString();
    }

    private static string? ColorAt(string?[]? colors, int i) =>
        colors is not null && i < colors.Length ? colors[i] : null;

    private static bool ChangedAt(bool[]? changed, int i) =>
        changed is not null && i < changed.Length && changed[i];

    /// <summary>Expande la segmentación intra-línea a un flag de "cambiado" por carácter.</summary>
    private static bool[] ChangedFlags(IReadOnlyList<InlineSegment> segments, int textLength)
    {
        var flags = new bool[textLength];
        int p = 0;
        foreach (var seg in segments)
            for (int k = 0; k < seg.Text.Length && p < textLength; k++, p++)
                flags[p] = seg.Changed;
        return flags;
    }

    private static string KindClass(DiffLineKind kind) => kind switch
    {
        DiffLineKind.Added    => "add",
        DiffLineKind.Deleted  => "del",
        DiffLineKind.Modified => "mod",
        _                     => "eq"
    };

    private static string Num(int? n) => n?.ToString() ?? "";

    private static string Escape(string s) => s
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;");

    // ─── Paletas (alineadas con los temas Light/Dark de la app) ──────────────
    private sealed record Theme(
        string Bg, string Fg, string HeadBg, string Border, string LineNo,
        string AddBg, string DelBg, string ModBg, string FillerBg,
        string AddInline, string DelInline, string BtnBg, string BtnHover);

    private static readonly Theme Light = new(
        Bg: "#ffffff", Fg: "#1f1f1f", HeadBg: "#f3f3f3", Border: "#e0e0e0", LineNo: "#9a9a9a",
        AddBg: "#e6ffed", DelBg: "#ffeef0", ModBg: "#fff8e1", FillerBg: "#fafafa",
        AddInline: "#acf2bd", DelInline: "#fdb8c0", BtnBg: "#ffffff", BtnHover: "#e8e8e8");

    private static readonly Theme Dark = new(
        Bg: "#1e1e1e", Fg: "#d4d4d4", HeadBg: "#252526", Border: "#3a3a3a", LineNo: "#6a6a6a",
        AddBg: "#122117", DelBg: "#2a1416", ModBg: "#2a2410", FillerBg: "#1a1a1a",
        AddInline: "#1f6f37", DelInline: "#7a2733", BtnBg: "#2d2d2d", BtnHover: "#3a3a3a");
}
