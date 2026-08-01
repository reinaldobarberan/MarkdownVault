using MarkdownVault.PluginSdk;

namespace MarkdownVault.Plugin.Highlight;

/// <summary>
/// Segundo plugin de PreviewAsset: resalta la sintaxis de los bloques de código
/// con highlight.js. Demuestra que sumar un plugin nuevo es el mismo molde que
/// Mermaid (tema CSS + librería + init).
/// </summary>
public sealed class HighlightPlugin : IPlugin
{
    public void Configure(IPluginContext context)
    {
        // 1) Tema (CSS) de highlight.js.
        context.AddPreviewAsset(new PreviewAsset
        {
            Kind      = AssetKind.Style,
            Source    = AssetSource.Url,
            Value     = "https://cdn.jsdelivr.net/gh/highlightjs/cdn-release@11.9.0/build/styles/github-dark.min.css",
            Placement = AssetPlacement.HeadEnd
        });

        // 2) Librería highlight.js.
        context.AddPreviewAsset(new PreviewAsset
        {
            Kind      = AssetKind.Script,
            Source    = AssetSource.Url,
            Value     = "https://cdn.jsdelivr.net/gh/highlightjs/cdn-release@11.9.0/build/highlight.min.js",
            Placement = AssetPlacement.HeadEnd
        });

        // 3) Init: resalta cada bloque, EXCEPTO los de Mermaid (los transforma su plugin).
        context.AddPreviewAsset(new PreviewAsset
        {
            Kind      = AssetKind.Script,
            Source    = AssetSource.Inline,
            Value     = InitScript,
            Placement = AssetPlacement.BodyEnd
        });

        context.Log("Syntax highlighting registrado (3 preview assets).");
    }

    private const string InitScript = """
        document.addEventListener("DOMContentLoaded", function () {
            if (typeof hljs === 'undefined') return;
            document.querySelectorAll('pre code:not(.language-mermaid)').forEach(function (el) {
                try { hljs.highlightElement(el); } catch (e) { /* ignore */ }
            });
        });
        """;
}
