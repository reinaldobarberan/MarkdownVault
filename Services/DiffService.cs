namespace MarkdownVault.Services;

/// <summary>Cómo se clasifica una fila del diff lado-a-lado.</summary>
public enum DiffLineKind
{
    /// <summary>Línea idéntica en ambos lados.</summary>
    Unchanged,
    /// <summary>Existe solo en el lado derecho (agregada).</summary>
    Added,
    /// <summary>Existe solo en el lado izquierdo (borrada).</summary>
    Deleted,
    /// <summary>Existe en ambos lados pero con contenido distinto (cambiada).</summary>
    Modified
}

/// <summary>
/// Un tramo de texto dentro de una línea, marcado como cambiado o no. Es lo que
/// permite el resaltado intra-línea estilo Beyond Compare: en una fila
/// <see cref="DiffLineKind.Modified"/> solo se pintan los caracteres que realmente
/// difieren, no la línea entera.
/// </summary>
public sealed class InlineSegment
{
    public string Text    { get; init; } = "";
    public bool   Changed { get; init; }
}

/// <summary>
/// Una fila del diff lado-a-lado. Las líneas iguales alinean izquierda y derecha; las
/// borradas dejan la derecha vacía; las agregadas dejan la izquierda vacía; las
/// modificadas alinean ambas y llevan además la segmentación intra-línea.
/// </summary>
public sealed class DiffRow
{
    public DiffLineKind Kind { get; init; }

    /// <summary>Número de línea 1-based en el archivo izquierdo, o null si no existe en él.</summary>
    public int? LeftLineNumber  { get; init; }
    /// <summary>Número de línea 1-based en el archivo derecho, o null si no existe en él.</summary>
    public int? RightLineNumber { get; init; }

    public string LeftText  { get; init; } = "";
    public string RightText { get; init; } = "";

    /// <summary>Segmentación intra-línea del lado izquierdo (solo poblada en <see cref="DiffLineKind.Modified"/>).</summary>
    public IReadOnlyList<InlineSegment> LeftSegments  { get; init; } = Array.Empty<InlineSegment>();
    /// <summary>Segmentación intra-línea del lado derecho (solo poblada en <see cref="DiffLineKind.Modified"/>).</summary>
    public IReadOnlyList<InlineSegment> RightSegments { get; init; } = Array.Empty<InlineSegment>();
}

/// <summary>
/// Motor de comparación de texto in-house (sin dependencias). Produce un diff
/// lado-a-lado por líneas usando LCS clásico, y para las líneas modificadas calcula
/// además un diff a nivel carácter para el resaltado estilo Beyond Compare.
/// </summary>
/// <remarks>
/// Usa DP LCS O(n·m) en tiempo y memoria. Suficiente para los tamaños de un vault de
/// notas Markdown; si en el futuro se comparan archivos muy grandes, migrar a Myers.
/// </remarks>
public sealed class DiffService
{
    private enum Op { Equal, Delete, Insert }

    /// <summary>Compara <paramref name="left"/> contra <paramref name="right"/> y devuelve las filas alineadas.</summary>
    public IReadOnlyList<DiffRow> Diff(string left, string right)
    {
        var leftLines  = SplitLines(left);
        var rightLines = SplitLines(right);

        var script = LcsScript(leftLines, rightLines, StringComparer.Ordinal);

        var rows   = new List<DiffRow>();
        var delBuf = new List<int>();   // índices de líneas borradas (izquierda) pendientes
        var insBuf = new List<int>();   // índices de líneas agregadas (derecha) pendientes

        void Flush()
        {
            // Empareja borradas+agregadas adyacentes como "modificadas" (alineadas 1:1),
            // igual que Beyond Compare. El sobrante queda como borrado o agregado puro.
            int pairs = Math.Min(delBuf.Count, insBuf.Count);
            for (int k = 0; k < pairs; k++)
                rows.Add(BuildModified(leftLines, rightLines, delBuf[k], insBuf[k]));
            for (int k = pairs; k < delBuf.Count; k++)
                rows.Add(BuildDeleted(leftLines, delBuf[k]));
            for (int k = pairs; k < insBuf.Count; k++)
                rows.Add(BuildAdded(rightLines, insBuf[k]));
            delBuf.Clear();
            insBuf.Clear();
        }

        foreach (var (op, li, ri) in script)
        {
            switch (op)
            {
                case Op.Equal:
                    Flush();
                    rows.Add(new DiffRow
                    {
                        Kind            = DiffLineKind.Unchanged,
                        LeftLineNumber  = li + 1,
                        RightLineNumber = ri + 1,
                        LeftText        = leftLines[li],
                        RightText       = rightLines[ri]
                    });
                    break;
                case Op.Delete: delBuf.Add(li); break;
                case Op.Insert: insBuf.Add(ri); break;
            }
        }
        Flush();

        return rows;
    }

    private static DiffRow BuildModified(IReadOnlyList<string> left, IReadOnlyList<string> right, int li, int ri)
    {
        var (leftSegs, rightSegs) = InlineDiff(left[li], right[ri]);
        return new DiffRow
        {
            Kind            = DiffLineKind.Modified,
            LeftLineNumber  = li + 1,
            RightLineNumber = ri + 1,
            LeftText        = left[li],
            RightText       = right[ri],
            LeftSegments    = leftSegs,
            RightSegments   = rightSegs
        };
    }

    private static DiffRow BuildDeleted(IReadOnlyList<string> left, int li) => new()
    {
        Kind           = DiffLineKind.Deleted,
        LeftLineNumber = li + 1,
        LeftText       = left[li]
    };

    private static DiffRow BuildAdded(IReadOnlyList<string> right, int ri) => new()
    {
        Kind            = DiffLineKind.Added,
        RightLineNumber = ri + 1,
        RightText       = right[ri]
    };

    /// <summary>
    /// Diff a nivel carácter entre dos líneas. Devuelve la segmentación de cada lado:
    /// los caracteres presentes solo en su lado se marcan <see cref="InlineSegment.Changed"/>.
    /// Caracteres contiguos con el mismo estado se agrupan en un único segmento.
    /// </summary>
    internal static (IReadOnlyList<InlineSegment> left, IReadOnlyList<InlineSegment> right) InlineDiff(
        string left, string right)
    {
        var leftChars  = left.ToCharArray();
        var rightChars = right.ToCharArray();
        var script     = LcsScript(leftChars, rightChars, EqualityComparer<char>.Default);

        var leftB  = new SegmentBuilder();
        var rightB = new SegmentBuilder();

        foreach (var (op, li, ri) in script)
        {
            switch (op)
            {
                case Op.Equal:
                    leftB.Append(leftChars[li], changed: false);
                    rightB.Append(rightChars[ri], changed: false);
                    break;
                case Op.Delete:
                    leftB.Append(leftChars[li], changed: true);
                    break;
                case Op.Insert:
                    rightB.Append(rightChars[ri], changed: true);
                    break;
            }
        }

        return (leftB.Build(), rightB.Build());
    }

    /// <summary>Acumula caracteres coalesciendo tramos contiguos con el mismo estado de cambio.</summary>
    private sealed class SegmentBuilder
    {
        private readonly List<InlineSegment> _segments = new();
        private readonly System.Text.StringBuilder _current = new();
        private bool _changed;
        private bool _has;

        public void Append(char c, bool changed)
        {
            if (_has && changed != _changed)
                Commit();
            _changed = changed;
            _has     = true;
            _current.Append(c);
        }

        private void Commit()
        {
            _segments.Add(new InlineSegment { Text = _current.ToString(), Changed = _changed });
            _current.Clear();
        }

        public IReadOnlyList<InlineSegment> Build()
        {
            if (_current.Length > 0) Commit();
            return _segments;
        }
    }

    /// <summary>
    /// LCS clásico con backtrack, devuelto como una secuencia ordenada de operaciones.
    /// El desempate prefiere <see cref="Op.Delete"/> antes que <see cref="Op.Insert"/>,
    /// de modo que en un bloque de cambios las borradas siempre preceden a las agregadas
    /// — eso permite emparejarlas como "modificadas" en un solo barrido.
    /// </summary>
    private static List<(Op op, int leftIndex, int rightIndex)> LcsScript<T>(
        IReadOnlyList<T> a, IReadOnlyList<T> b, IEqualityComparer<T> cmp)
    {
        int n = a.Count, m = b.Count;
        var dp = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                dp[i, j] = cmp.Equals(a[i], b[j])
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);

        var ops = new List<(Op, int, int)>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (cmp.Equals(a[x], b[y])) { ops.Add((Op.Equal, x, y)); x++; y++; }
            else if (dp[x + 1, y] >= dp[x, y + 1]) { ops.Add((Op.Delete, x, y)); x++; }
            else { ops.Add((Op.Insert, x, y)); y++; }
        }
        while (x < n) { ops.Add((Op.Delete, x, y)); x++; }
        while (y < m) { ops.Add((Op.Insert, x, y)); y++; }
        return ops;
    }

    /// <summary>Divide en líneas normalizando CRLF/CR a LF primero, para que el diff no marque falsos cambios por fin de línea.</summary>
    internal static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
}
