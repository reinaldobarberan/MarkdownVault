using MarkdownVault.Services;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Cubre el núcleo puro de "copiar línea al otro archivo": para cada tipo de fila y en
/// ambos sentidos (◀/▶), el destino debe quedar igual a la fuente en esa posición —
/// insertando, reemplazando o borrando según corresponda.
/// </summary>
public class DiffMergeTests
{
    private static readonly DiffService Svc = new();

    private static (string left, string right) Apply(
        string left, string right, int row, MergeDirection dir)
    {
        var rows = Svc.Diff(left, right);
        return DiffMerge.Apply(rows, row, dir, left, right);
    }

    [Fact]
    public void Modified_ToRight_replaces_right_with_left()
    {
        var (left, right) = Apply("x\nfoo\nz", "x\nbar\nz", row: 1, MergeDirection.ToRight);

        Assert.Equal("x\nfoo\nz", right);
        Assert.Equal("x\nfoo\nz", left);   // la izquierda no se toca
    }

    [Fact]
    public void Modified_ToLeft_replaces_left_with_right()
    {
        var (left, right) = Apply("x\nfoo\nz", "x\nbar\nz", row: 1, MergeDirection.ToLeft);

        Assert.Equal("x\nbar\nz", left);
        Assert.Equal("x\nbar\nz", right);  // la derecha no se toca
    }

    [Fact]
    public void Deleted_ToRight_inserts_the_line_into_the_right()
    {
        // "b" existe solo a la izquierda (fila 1).
        var (_, right) = Apply("a\nb\nc", "a\nc", row: 1, MergeDirection.ToRight);
        Assert.Equal("a\nb\nc", right);
    }

    [Fact]
    public void Deleted_ToLeft_removes_the_line_from_the_left()
    {
        var (left, _) = Apply("a\nb\nc", "a\nc", row: 1, MergeDirection.ToLeft);
        Assert.Equal("a\nc", left);
    }

    [Fact]
    public void Added_ToLeft_inserts_the_line_into_the_left()
    {
        // "b" existe solo a la derecha (fila 1).
        var (left, _) = Apply("a\nc", "a\nb\nc", row: 1, MergeDirection.ToLeft);
        Assert.Equal("a\nb\nc", left);
    }

    [Fact]
    public void Added_ToRight_removes_the_line_from_the_right()
    {
        var (_, right) = Apply("a\nc", "a\nb\nc", row: 1, MergeDirection.ToRight);
        Assert.Equal("a\nc", right);
    }

    [Fact]
    public void Unchanged_row_is_a_no_op()
    {
        var (left, right) = Apply("same", "same", row: 0, MergeDirection.ToRight);
        Assert.Equal("same", left);
        Assert.Equal("same", right);
    }

    [Fact]
    public void Out_of_range_row_is_a_no_op()
    {
        var rows = Svc.Diff("a", "b");
        var (left, right) = DiffMerge.Apply(rows, 99, MergeDirection.ToRight, "a", "b");
        Assert.Equal("a", left);
        Assert.Equal("b", right);
    }

    [Fact]
    public void Block_ToRight_inserts_the_whole_deleted_block()
    {
        // "b" y "c" existen solo a la izquierda (bloque de 2 filas). Clic en cualquiera copia ambas.
        var rows = Svc.Diff("a\nb\nc\nd", "a\nd");
        var (_, right) = DiffMerge.ApplyBlock(rows, 2, MergeDirection.ToRight, "a\nb\nc\nd", "a\nd");
        Assert.Equal("a\nb\nc\nd", right);
    }

    [Fact]
    public void Block_ToLeft_removes_the_whole_block_from_left()
    {
        var rows = Svc.Diff("a\nb\nc\nd", "a\nd");
        var (left, _) = DiffMerge.ApplyBlock(rows, 1, MergeDirection.ToLeft, "a\nb\nc\nd", "a\nd");
        Assert.Equal("a\nd", left);
    }

    [Fact]
    public void Block_replaces_a_multiline_modified_run()
    {
        // Dos líneas modificadas seguidas → un solo bloque; se reemplaza completo.
        var rows = Svc.Diff("h\nx1\nx2\nt", "h\ny1\ny2\nt");
        var (_, right) = DiffMerge.ApplyBlock(rows, 1, MergeDirection.ToRight, "h\nx1\nx2\nt", "h\ny1\ny2\nt");
        Assert.Equal("h\nx1\nx2\nt", right);
    }

    [Fact]
    public void Preserves_crlf_of_the_target_when_recomposing()
    {
        // Destino (derecha) usa CRLF; tras insertar debe seguir en CRLF.
        var rows = Svc.Diff("a\nb\nc", "a\r\nc");
        var (_, right) = DiffMerge.Apply(rows, 1, MergeDirection.ToRight, "a\nb\nc", "a\r\nc");
        Assert.Equal("a\r\nb\r\nc", right);
    }
}
