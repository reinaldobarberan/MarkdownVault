using System.Linq;
using MarkdownVault.Services;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Cubre el motor de comparación in-house: alineación lado-a-lado por líneas (LCS) y el
/// diff intra-línea a nivel carácter que habilita el resaltado estilo Beyond Compare.
/// </summary>
public class DiffServiceTests
{
    private static readonly DiffService Svc = new();

    [Fact]
    public void Identical_text_is_all_unchanged_with_paired_line_numbers()
    {
        var rows = Svc.Diff("a\nb\nc", "a\nb\nc");

        Assert.All(rows, r => Assert.Equal(DiffLineKind.Unchanged, r.Kind));
        Assert.Equal(new int?[] { 1, 2, 3 }, rows.Select(r => r.LeftLineNumber));
        Assert.Equal(new int?[] { 1, 2, 3 }, rows.Select(r => r.RightLineNumber));
    }

    [Fact]
    public void Added_line_appears_only_on_the_right()
    {
        var rows = Svc.Diff("a\nc", "a\nb\nc");

        var added = Assert.Single(rows.Where(r => r.Kind == DiffLineKind.Added));
        Assert.Equal("b", added.RightText);
        Assert.Equal("", added.LeftText);
        Assert.Null(added.LeftLineNumber);
        Assert.Equal(2, added.RightLineNumber);
    }

    [Fact]
    public void Deleted_line_appears_only_on_the_left()
    {
        var rows = Svc.Diff("a\nb\nc", "a\nc");

        var deleted = Assert.Single(rows.Where(r => r.Kind == DiffLineKind.Deleted));
        Assert.Equal("b", deleted.LeftText);
        Assert.Equal("", deleted.RightText);
        Assert.Equal(2, deleted.LeftLineNumber);
        Assert.Null(deleted.RightLineNumber);
    }

    [Fact]
    public void Changed_line_is_a_single_modified_row_aligning_both_sides()
    {
        var rows = Svc.Diff("hello world", "hello there");

        var mod = Assert.Single(rows.Where(r => r.Kind == DiffLineKind.Modified));
        Assert.Equal("hello world", mod.LeftText);
        Assert.Equal("hello there", mod.RightText);
        Assert.Equal(1, mod.LeftLineNumber);
        Assert.Equal(1, mod.RightLineNumber);

        // Los segmentos siempre reconstruyen la línea original.
        Assert.Equal(mod.LeftText,  string.Concat(mod.LeftSegments.Select(s => s.Text)));
        Assert.Equal(mod.RightText, string.Concat(mod.RightSegments.Select(s => s.Text)));
        // Y marcan algo como cambiado en cada lado (no toda la línea es igual).
        Assert.Contains(mod.LeftSegments,  s => s.Changed);
        Assert.Contains(mod.RightSegments, s => s.Changed);
    }

    [Fact]
    public void Crlf_and_lf_do_not_produce_false_differences()
    {
        var rows = Svc.Diff("a\r\nb\r\nc", "a\nb\nc");

        Assert.All(rows, r => Assert.Equal(DiffLineKind.Unchanged, r.Kind));
    }

    [Fact]
    public void InlineDiff_marks_only_the_differing_characters()
    {
        var (left, right) = DiffService.InlineDiff("cat", "cot");

        // 'c' y 't' comunes; 'a' solo a la izquierda, 'o' solo a la derecha.
        Assert.Equal("cat", string.Concat(left.Select(s => s.Text)));
        Assert.Equal("cot", string.Concat(right.Select(s => s.Text)));

        Assert.Equal("a", string.Concat(left.Where(s => s.Changed).Select(s => s.Text)));
        Assert.Equal("o", string.Concat(right.Where(s => s.Changed).Select(s => s.Text)));
    }

    [Fact]
    public void Common_prefix_stays_unchanged_in_a_modified_row()
    {
        var (left, _) = DiffService.InlineDiff("hello world", "hello there");

        // El prefijo "hello " no debe marcarse como cambiado.
        var firstChanged = left.TakeWhile(s => !s.Changed);
        Assert.StartsWith("hello", string.Concat(firstChanged.Select(s => s.Text)));
    }
}
