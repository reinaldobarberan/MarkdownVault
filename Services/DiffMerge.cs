namespace MarkdownVault.Services;

/// <summary>Sentido de una operación de copiar-línea entre los dos archivos comparados.</summary>
public enum MergeDirection
{
    /// <summary>La derecha pasa a ser igual a la izquierda en esa fila (fuente = izquierda).</summary>
    ToRight,
    /// <summary>La izquierda pasa a ser igual a la derecha en esa fila (fuente = derecha).</summary>
    ToLeft
}

/// <summary>
/// Aplica una operación de merge estilo Beyond Compare sobre UNA fila del diff: hace que
/// el lado destino quede igual al lado fuente en esa posición alineada. Función pura —
/// recibe los textos completos y devuelve los nuevos, sin tocar UI ni estado. Con eso, un
/// mismo botón cubre los tres casos:
///   • fila solo-en-fuente  → INSERTA la línea en el destino;
///   • fila solo-en-destino → BORRA la línea del destino;
///   • fila modificada      → REEMPLAZA la línea del destino con la de la fuente.
/// </summary>
public static class DiffMerge
{
    /// <param name="rows">Diff vigente de (<paramref name="left"/>, <paramref name="right"/>).</param>
    /// <param name="rowIndex">Índice de la fila sobre la que se actúa.</param>
    /// <returns>Los textos completos resultantes. Idempotente/seguro fuera de rango o sobre filas iguales.</returns>
    public static (string left, string right) Apply(
        IReadOnlyList<DiffRow> rows, int rowIndex, MergeDirection direction, string left, string right)
    {
        if (rowIndex < 0 || rowIndex >= rows.Count) return (left, right);

        var row = rows[rowIndex];
        if (row.Kind == DiffLineKind.Unchanged) return (left, right);

        var leftLines  = new List<string>(DiffService.SplitLines(left));
        var rightLines = new List<string>(DiffService.SplitLines(right));

        if (direction == MergeDirection.ToRight)
            MakeTargetMatchSource(rows, rowIndex, leftLines, rightLines,
                sourceLineNo: row.LeftLineNumber, targetLineNo: row.RightLineNumber, targetIsRight: true);
        else
            MakeTargetMatchSource(rows, rowIndex, rightLines, leftLines,
                sourceLineNo: row.RightLineNumber, targetLineNo: row.LeftLineNumber, targetIsRight: false);

        return (string.Join(NewlineOf(left), leftLines), string.Join(NewlineOf(right), rightLines));
    }

    /// <summary>
    /// Como <see cref="Apply"/>, pero opera sobre el BLOQUE completo de diferencias contiguas
    /// que contiene <paramref name="rowIndex"/>: reemplaza el rango del destino por el rango
    /// de la fuente de una sola vez. Útil para empujar un cambio de varias líneas (p. ej. una
    /// función entera al comparar código) en un gesto. Un bloque es una corrida maximal de
    /// filas no-iguales delimitada por filas iguales (o los extremos).
    /// </summary>
    public static (string left, string right) ApplyBlock(
        IReadOnlyList<DiffRow> rows, int rowIndex, MergeDirection direction, string left, string right)
    {
        if (rowIndex < 0 || rowIndex >= rows.Count) return (left, right);
        if (rows[rowIndex].Kind == DiffLineKind.Unchanged) return (left, right);

        // Expandir a los límites del bloque de diferencias contiguas.
        int start = rowIndex, end = rowIndex;
        while (start - 1 >= 0        && rows[start - 1].Kind != DiffLineKind.Unchanged) start--;
        while (end   + 1 < rows.Count && rows[end   + 1].Kind != DiffLineKind.Unchanged) end++;

        var leftLines  = new List<string>(DiffService.SplitLines(left));
        var rightLines = new List<string>(DiffService.SplitLines(right));

        if (direction == MergeDirection.ToRight)
            ReplaceBlock(rows, start, end, leftLines, rightLines,
                sourceSel: r => r.LeftLineNumber, targetSel: r => r.RightLineNumber, targetIsRight: true);
        else
            ReplaceBlock(rows, start, end, rightLines, leftLines,
                sourceSel: r => r.RightLineNumber, targetSel: r => r.LeftLineNumber, targetIsRight: false);

        return (string.Join(NewlineOf(left), leftLines), string.Join(NewlineOf(right), rightLines));
    }

    private static void ReplaceBlock(
        IReadOnlyList<DiffRow> rows, int start, int end,
        List<string> sourceLines, List<string> targetLines,
        Func<DiffRow, int?> sourceSel, Func<DiffRow, int?> targetSel, bool targetIsRight)
    {
        // Líneas de la fuente dentro del bloque (en orden).
        var src = new List<string>();
        for (int i = start; i <= end; i++)
            if (sourceSel(rows[i]) is int ln) src.Add(sourceLines[ln - 1]);

        // Rango del destino a reemplazar: arranca tras las líneas destino previas al bloque,
        // y abarca tantas líneas destino como haya en el bloque.
        int targetStart = CountPriorTargetLines(rows, start, targetIsRight);
        int targetCount = 0;
        for (int i = start; i <= end; i++)
            if (targetSel(rows[i]) is not null) targetCount++;

        targetStart = Math.Min(targetStart, targetLines.Count);
        targetCount = Math.Min(targetCount, targetLines.Count - targetStart);

        targetLines.RemoveRange(targetStart, targetCount);
        targetLines.InsertRange(targetStart, src);
    }

    private static void MakeTargetMatchSource(
        IReadOnlyList<DiffRow> rows, int rowIndex,
        List<string> sourceLines, List<string> targetLines,
        int? sourceLineNo, int? targetLineNo, bool targetIsRight)
    {
        if (sourceLineNo is null)
        {
            // La fuente no tiene línea acá → el destino debe perder la suya (borrar).
            if (targetLineNo is int tno) targetLines.RemoveAt(tno - 1);
            return;
        }

        var sourceText = sourceLines[sourceLineNo.Value - 1];

        if (targetLineNo is int existing)
        {
            // Ambos presentes (modificada) → reemplazar la línea destino por la de la fuente.
            targetLines[existing - 1] = sourceText;
        }
        else
        {
            // El destino no tiene línea acá → insertar en la posición alineada. Esa posición es
            // la cantidad de líneas del destino que aparecen ANTES de esta fila en el diff.
            int insertAt = CountPriorTargetLines(rows, rowIndex, targetIsRight);
            targetLines.Insert(Math.Min(insertAt, targetLines.Count), sourceText);
        }
    }

    private static int CountPriorTargetLines(IReadOnlyList<DiffRow> rows, int rowIndex, bool targetIsRight)
    {
        int count = 0;
        for (int i = 0; i < rowIndex; i++)
        {
            var n = targetIsRight ? rows[i].RightLineNumber : rows[i].LeftLineNumber;
            if (n is not null) count++;
        }
        return count;
    }

    /// <summary>Preserva la convención de fin de línea dominante del texto destino al recomponerlo.</summary>
    private static string NewlineOf(string text) => text.Contains("\r\n") ? "\r\n" : "\n";
}
