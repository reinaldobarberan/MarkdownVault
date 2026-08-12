using MarkdownVault.Services;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>Cubre el render HTML lado-a-lado: estructura, títulos, clases por tipo y escape.</summary>
public class DiffHtmlRendererTests
{
    private static readonly DiffService Svc = new();

    [Fact]
    public void Renders_full_document_with_both_titles()
    {
        var rows = Svc.Diff("a", "a");
        var html = DiffHtmlRenderer.Render(rows, "izquierda.md", "derecha.md", isDark: false);

        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("izquierda.md", html);
        Assert.Contains("derecha.md", html);
    }

    [Fact]
    public void Modified_row_emits_inline_change_spans()
    {
        var rows = Svc.Diff("cat", "cot");
        var html = DiffHtmlRenderer.Render(rows, "l", "r", isDark: false);

        Assert.Contains("class=\"row mod\"", html);
        Assert.Contains("class=\"chg\"", html);
    }

    [Fact]
    public void Added_and_deleted_rows_get_their_classes()
    {
        var addHtml = DiffHtmlRenderer.Render(Svc.Diff("a", "a\nb"), "l", "r", isDark: false);
        var delHtml = DiffHtmlRenderer.Render(Svc.Diff("a\nb", "a"), "l", "r", isDark: false);

        Assert.Contains("class=\"row add\"", addHtml);
        Assert.Contains("class=\"row del\"", delHtml);
    }

    [Fact]
    public void Html_special_characters_are_escaped()
    {
        // Un tag único que no aparece en el shell de la página; como fila borrada (derecha
        // vacía) queda en un único segmento, así que su forma escapada sale contigua.
        var rows = Svc.Diff("<xyz>", "");
        var html = DiffHtmlRenderer.Render(rows, "l", "r", isDark: false);

        Assert.Contains("&lt;xyz&gt;", html);
        Assert.DoesNotContain("<xyz>", html);   // nunca como tag crudo en el contenido
    }
}
