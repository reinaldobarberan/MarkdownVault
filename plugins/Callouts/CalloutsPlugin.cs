using MarkdownVault.PluginSdk;

namespace MarkdownVault.Plugin.Callouts;

/// <summary>
/// Primer plugin que usa el punto de extensión de Markdig: aporta una extensión
/// que marca los blockquotes <c>[!type]</c> y un CSS que los estiliza. Demuestra
/// que el pipeline de Markdig es extensible por plugins.
/// </summary>
public sealed class CalloutsPlugin : IPlugin
{
    public void Configure(IPluginContext context)
    {
        // Extensión de sintaxis (Markdig): marca los blockquotes [!type] con clases.
        context.AddMarkdownExtension(new CalloutContribution());

        // CSS que convierte esas clases en cajas de aviso.
        context.AddPreviewAsset(new PreviewAsset
        {
            Kind      = AssetKind.Style,
            Source    = AssetSource.Inline,
            Value     = Css,
            Placement = AssetPlacement.HeadEnd
        });

        context.Log("Callouts registrado (1 extensión Markdown + CSS).");
    }

    // Estiliza TANTO los alerts nativos de Markdig (.markdown-alert*, que la app
    // dejaba sin estilo) COMO los callouts que aporta la extensión del plugin
    // (.callout*, la forma Obsidian con título en línea).
    private const string Css = """
        .markdown-alert, .callout {
            border-left: 4px solid #539bf5;
            border-radius: 6px;
            padding: 12px 16px;
            margin: 16px 0;
            background: rgba(83,155,245,0.08);
        }
        .markdown-alert > :first-child, .callout > :first-child { margin-top: 0; }
        .markdown-alert > :last-child,  .callout > :last-child  { margin-bottom: 0; }

        /* Título de los alerts nativos (ya incluye ícono SVG + texto) */
        .markdown-alert-title { font-weight:600; display:flex; align-items:center; gap:6px; margin-bottom:6px; }
        .markdown-alert-title svg { fill: currentColor; }

        /* Título sintético para los callouts del plugin (el marcador ya fue quitado) */
        .callout::before { display:block; font-weight:600; margin-bottom:6px; font-size:0.9em; }

        /* ── Colores por tipo (alert nativo + callout del plugin) ── */
        .markdown-alert-note, .callout-note { border-left-color:#539bf5; background:rgba(83,155,245,0.08); }
        .markdown-alert-note .markdown-alert-title, .callout-note::before { color:#539bf5; }
        .callout-note::before { content:"ℹ  Nota"; }

        .markdown-alert-tip, .callout-tip { border-left-color:#57ab5a; background:rgba(87,171,90,0.08); }
        .markdown-alert-tip .markdown-alert-title, .callout-tip::before { color:#57ab5a; }
        .callout-tip::before { content:"💡 Tip"; }

        .markdown-alert-important, .callout-important { border-left-color:#986ee2; background:rgba(152,110,226,0.10); }
        .markdown-alert-important .markdown-alert-title, .callout-important::before { color:#986ee2; }
        .callout-important::before { content:"❗ Importante"; }

        .markdown-alert-warning, .callout-warning { border-left-color:#c69026; background:rgba(198,144,38,0.10); }
        .markdown-alert-warning .markdown-alert-title, .callout-warning::before { color:#c69026; }
        .callout-warning::before { content:"⚠  Advertencia"; }

        .markdown-alert-caution, .callout-danger, .callout-caution { border-left-color:#e5534b; background:rgba(229,83,75,0.10); }
        .markdown-alert-caution .markdown-alert-title, .callout-danger::before, .callout-caution::before { color:#e5534b; }
        .callout-danger::before, .callout-caution::before { content:"🔥 Peligro"; }
        """;
}

/// <summary>Envuelve la extensión de Markdig sin filtrar el tipo al contrato del SDK.</summary>
internal sealed class CalloutContribution : IMarkdownContribution
{
    public object CreateMarkdigExtension() => new CalloutMarkdigExtension();
}
