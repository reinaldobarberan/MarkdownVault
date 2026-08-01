using System.Text.RegularExpressions;
using Markdig;
using Markdig.Helpers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MarkdownVault.Plugin.Callouts;

/// <summary>
/// Extensión de Markdig que detecta blockquotes que empiezan con <c>[!type]</c>
/// (estilo Obsidian) y les agrega clases CSS (<c>callout callout-{type}</c>),
/// quitando el marcador del texto. El estilo lo pone el CSS del plugin.
/// </summary>
internal sealed class CalloutMarkdigExtension : IMarkdownExtension
{
    private static readonly Regex Marker = new(@"^\s*\[!(\w+)\]\s?", RegexOptions.Compiled);

    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        pipeline.DocumentProcessed -= OnDocumentProcessed;
        pipeline.DocumentProcessed += OnDocumentProcessed;
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer) { }

    private static void OnDocumentProcessed(MarkdownDocument document)
    {
        foreach (var quote in document.Descendants<QuoteBlock>())
        {
            var type = ProcessCallout(quote);
            if (type is null) continue;

            var attrs = quote.GetAttributes();
            attrs.AddClass("callout");
            attrs.AddClass("callout-" + type.ToLowerInvariant());
            attrs.AddProperty("data-callout", type.ToLowerInvariant());
        }
    }

    /// <summary>Detecta [!type] al inicio del quote y lo quita del texto. Devuelve el tipo o null.</summary>
    private static string? ProcessCallout(QuoteBlock quote)
    {
        if (quote.Count == 0 || quote[0] is not ParagraphBlock para || para.Inline is null)
            return null;

        var literals = para.Inline.Descendants<LiteralInline>().ToList();
        if (literals.Count == 0) return null;

        var text  = string.Concat(literals.Select(l => l.Content.ToString()));
        var match = Marker.Match(text);
        if (!match.Success) return null;

        RemoveLeadingChars(literals, match.Length);
        return match.Groups[1].Value;
    }

    /// <summary>Elimina los primeros <paramref name="count"/> caracteres de la cadena de literales.</summary>
    private static void RemoveLeadingChars(List<LiteralInline> literals, int count)
    {
        foreach (var lit in literals)
        {
            if (count <= 0) break;

            var s = lit.Content.ToString();
            if (count >= s.Length)
            {
                count -= s.Length;
                lit.Content = new StringSlice(string.Empty);
            }
            else
            {
                lit.Content = new StringSlice(s.Substring(count));
                count = 0;
            }
        }
    }
}
