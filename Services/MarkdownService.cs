using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdownVault.Helpers;
using MarkdownVault.PluginSdk;
using MarkdownVault.Services.Plugins;

namespace MarkdownVault.Services;

/// <summary>Converts Markdown text to a self-contained HTML page using Markdig.</summary>
public class MarkdownService
{
    // Registry de plugins: provee los PreviewAssets (CSS/JS) a inyectar en la página
    // de preview Y las extensiones Markdown (Markdig) del pipeline. Antes Mermaid y el
    // pipeline estaban hardcodeados; ahora salen de los plugins habilitados.
    private readonly PluginRegistry _registry;

    // Pipeline cacheado; se invalida cuando cambia el set de plugins (activar/desactivar).
    private MarkdownPipeline? _pipeline;

    public MarkdownService(PluginRegistry registry)
    {
        _registry = registry;
        _registry.Changed += () => _pipeline = null;   // rebuild on next render
    }

    /// <summary>
    /// Construye (y cachea) el pipeline: extensiones base + las que aporten los
    /// plugins de sintaxis habilitados (vía <see cref="IMarkdownContribution"/>).
    /// </summary>
    private MarkdownPipeline GetPipeline()
    {
        if (_pipeline is not null) return _pipeline;

        var builder = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub);

        // Host-owned block-marker extension (design decision #3), registered BEFORE the plugin
        // loop below (Q6) so a `^id` marker is stripped and its id stamped against the ORIGINAL
        // paragraph structure — before a plugin extension (e.g. Callouts) gets a chance to
        // restructure it. Stateless; safe to add fresh on every rebuild.
        builder.Extensions.Add(new BlockAnchorExtension());

        foreach (var contribution in _registry.MarkdownContributions)
        {
            if (contribution.CreateMarkdigExtension() is IMarkdownExtension ext)
                builder.Extensions.Add(ext);
        }

        return _pipeline = builder.Build();
    }

    /// <summary>
    /// Renders <paramref name="markdown"/> to a full HTML document.
    /// When <paramref name="vaultRoot"/> is provided the virtual host base URL
    /// (<c>http://vault.local/</c>) is injected so that relative images resolve.
    /// </summary>
    public string RenderToHtml(string markdown, bool isDarkTheme, string? vaultRoot = null)
    {
        return WrapInPage(RenderBody(markdown), isDarkTheme, vaultRoot);
    }

    /// <summary>
    /// Renders <paramref name="markdown"/> to just the inner HTML fragment (no page shell).
    /// Used for in-place preview updates: the fragment is pushed into the existing page's
    /// content container via JS, avoiding a full reload (and its flash/scroll reset).
    /// It is exactly the body that <see cref="RenderToHtml"/> embeds.
    /// </summary>
    public string RenderBody(string markdown)
    {
        var processed = PreprocessWikiLinks(markdown);
        return Markdig.Markdown.ToHtml(processed, GetPipeline());
    }

    // ─── Anchor lookups (pure — link-anchors change) ─────────────────────────

    /// <summary>
    /// Parses <paramref name="markdown"/> against THIS service's own pipeline — same
    /// extensions the preview renders with (auto-identifiers, plugin contributions, our own
    /// <see cref="BlockAnchorExtension"/>) — so heading/block ids never drift from what the
    /// preview actually shows (design decision #6: "zero drift, composes with plugin
    /// extensions for free"). Pure: no HTML rendering, no WPF, no file I/O.
    /// </summary>
    /// <remarks>
    /// ⚠ EL AST QUE DEVUELVE NO ES EL DEL TEXTO QUE EL USUARIO TIENE EN PANTALLA.
    ///
    /// MECANISMO (esto es lo que hay que entender, no una regla que memorizar): lo que se
    /// parsea es <see cref="PreprocessWikiLinks"/>(markdown), es decir una COPIA REESCRITA.
    /// Un <c>[[audio parte 001#^8jlekw]]</c> (27 caracteres) se expande a
    /// <c>[audio parte 001](&lt;audio parte 001.md#^8jlekw&gt;)</c> (muchos más). A partir de
    /// ese punto, TODO <c>SourceSpan</c> del AST está corrido: son índices dentro de la cadena
    /// reescrita, NO dentro del documento vivo de AvalonEdit. Usar <c>block.Span.Start/End</c>
    /// contra <c>TextEditor.Document</c> tira <c>ArgumentOutOfRangeException</c> cuando el
    /// corrimiento se pasa del final — y, PEOR, acierta en silencio en el lugar equivocado
    /// cuando la nota es lo bastante larga como para no tirar la excepción.
    ///
    /// QUÉ SÍ SOBREVIVE: los ids (por eso el nombre "preview ast": los ids son exactamente los
    /// que el preview renderiza, decisión #6) y las LÍNEAS. <see cref="PreprocessWikiLinks"/>
    /// sustituye siempre DENTRO de una línea — los regex de wikilink excluyen <c>\r\n</c> a
    /// propósito y los code spans se copian literales — así que nunca agrega ni saca saltos de
    /// línea. Por eso <c>block.Line</c> es válido contra el documento vivo y <c>block.Span</c>
    /// no lo es.
    ///
    /// REGLA DERIVADA: la unidad de intercambio entre este AST y el documento vivo es la
    /// LÍNEA. Para convertirla a offset, pedíselo al documento vivo:
    /// <c>TextEditor.Document.GetLineByNumber(line + 1).Offset</c> (Markdig cuenta líneas desde
    /// 0, AvalonEdit desde 1). Ver <see cref="Helpers.AnchorLocator"/>.
    /// </remarks>
    public MarkdownDocument ParsePreviewAst(string markdown) =>
        Markdig.Markdown.Parse(PreprocessWikiLinks(markdown), GetPipeline());

    /// <summary>
    /// Every heading's resolved anchor id, plain text (the heading-text fallback,
    /// decision #6), and source line — for anchor resolution (<see cref="AnchorLocator"/>) and,
    /// later, the link picker's heading list.
    /// </summary>
    public IReadOnlyList<(string Id, string Text, int Line)> GetHeadings(string markdown)
    {
        var doc = ParsePreviewAst(markdown);
        var headings = new List<(string, string, int)>();
        foreach (var heading in doc.Descendants<HeadingBlock>())
        {
            var id   = heading.TryGetAttributes()?.Id ?? string.Empty;
            var text = PlainText(heading.Inline);
            headings.Add((id, text, heading.Line));
        }
        return headings;
    }

    /// <summary>
    /// Every recognized block marker — namespaced id stripped back to its bare form
    /// (<c>"mv-b-a1b2c3"</c> → <c>"a1b2c3"</c>) — and its source line. Used by
    /// <see cref="AnchorLocator"/> and, later, the link picker's "already-marked paragraphs"
    /// list and the "Copiar enlace a este párrafo" collision check.
    /// </summary>
    public IReadOnlyList<(string Id, int Line)> GetBlockMarkers(string markdown)
    {
        var doc = ParsePreviewAst(markdown);
        var markers = new List<(string, int)>();
        foreach (var paragraph in doc.Descendants<ParagraphBlock>())
        {
            var id = paragraph.TryGetAttributes()?.Id;
            if (id is not null && id.StartsWith(BlockAnchorExtension.IdPrefix, StringComparison.Ordinal))
                markers.Add((id[BlockAnchorExtension.IdPrefix.Length..], paragraph.Line));
        }
        return markers;
    }

    private static string PlainText(ContainerInline? inline)
    {
        if (inline is null) return string.Empty;
        var sb = new System.Text.StringBuilder();
        foreach (var literal in inline.Descendants<LiteralInline>())
            sb.Append(literal.Content.ToString());
        return sb.ToString();
    }

    // ─── Wikilink preprocessing ──────────────────────────────────────────────

    // Fenced (```...```) and inline (`...`) code spans. Wikilink rewriting must skip
    // these so code stays intact — notably Mermaid's [[subroutine]] node shape, which
    // would otherwise be mangled into a Markdown link and break the diagram.
    private static readonly Regex CodeSpanRegex = new(
        @"```[\s\S]*?```|`[^`]*`", RegexOptions.Compiled);

    // [[target]] or [[target|display]].
    //
    // Los `\r\n` excluidos NO son cosmética: son el contrato que sostiene todo
    // ParsePreviewAst. Una clase negada como [^\]|] también matchea saltos de línea, así que
    // un `[[audio\nparte]]` se colapsaba a UNA línea al reescribirse (el .Trim() del display
    // se come el salto) y el AST quedaba corrido EN LÍNEAS, no sólo en offsets — es decir,
    // se rompía la única coordenada que el resto del código puede usar contra el documento
    // vivo. Acotando el match a una sola línea, "el preprocesado no agrega ni saca saltos de
    // línea" pasa de ser una observación a ser una propiedad estructural.
    // Efecto secundario deseado: un `[[...]]` partido en dos líneas ya no se convierte en
    // enlace, que es exactamente lo que hace Obsidian.
    private static readonly Regex WikiLinkRegex = new(
        @"\[\[([^\]|\r\n]+)(?:\|([^\]\r\n]+))?\]\]", RegexOptions.Compiled);

    /// <summary>
    /// Converts <c>[[target]]</c> wikilinks into standard Markdown links
    /// (<c>[target](target.md)</c>) so Markdig renders them as clickable
    /// <c>&lt;a&gt;</c> tags resolved via the <c>vault.local</c> base URL.
    /// Also supports <c>[[target|display text]]</c> syntax. Content inside code
    /// spans/blocks is left untouched.
    /// </summary>
    private static string PreprocessWikiLinks(string markdown)
    {
        var sb   = new System.Text.StringBuilder(markdown.Length);
        int last = 0;

        foreach (Match code in CodeSpanRegex.Matches(markdown))
        {
            // Rewrite wikilinks only in the prose before this code span…
            sb.Append(ConvertWikiLinks(markdown.Substring(last, code.Index - last)));
            // …and copy the code span verbatim.
            sb.Append(code.Value);
            last = code.Index + code.Length;
        }
        sb.Append(ConvertWikiLinks(markdown.Substring(last)));

        return sb.ToString();
    }

    /// <remarks>
    /// The target is split via <see cref="LinkTarget"/> — the SAME parser every other call
    /// site uses — before the href is built. Previously this blindly appended <c>.md</c> to
    /// the whole raw target, so <c>[[#Conclusiones]]</c> (an intra-document anchor) rendered
    /// as <c>href="#Conclusiones.md"</c>: wrong AND not routed as an in-page scroll at all
    /// (link-anchors change, "Defect 1"). Splitting first means an empty note half now
    /// produces a bare <c>#anchor</c> href, and a heading/block anchor on another note keeps
    /// its <c>#anchor</c> suffix instead of being swallowed into the filename.
    /// </remarks>
    private static string ConvertWikiLinks(string text) =>
        WikiLinkRegex.Replace(text, match =>
        {
            var target = match.Groups[1].Value.Trim();
            var parsed = LinkTarget.Parse(target);

            // Default display is the note name (not the full path) for clean link text; for an
            // intra-document anchor there is no note, so fall back to the anchor text itself.
            var display = match.Groups[2].Success
                ? match.Groups[2].Value.Trim()
                : parsed.Note.Length > 0
                    ? System.IO.Path.GetFileNameWithoutExtension(parsed.Note)
                    : parsed.Anchor ?? target;

            var href = BuildWikiLinkHref(parsed);
            // CommonMark: link destinations that contain spaces or parentheses must
            // be wrapped in angle brackets, otherwise the link isn't recognized.
            if (href.IndexOfAny([' ', '(', ')']) >= 0)
                href = $"<{href}>";
            return $"[{display}]({href})";
        });

    /// <summary>
    /// Rebuilds an href from an already-split <see cref="LinkTarget"/>: <c>.md</c> is appended
    /// only to a non-empty note half that has no extension, and the anchor (if any) is
    /// reattached as a <c>#fragment</c> — never baked into the note half itself, which is what
    /// let the junk-file defect happen upstream in <c>FileService.ResolveInternalLink</c>. A
    /// bare <c>^id</c> becomes <c>#^id</c> in the href; WebView2/the browser will
    /// percent-encode the <c>^</c> on navigation, and the click handler
    /// (<c>MainWindow.xaml.cs</c>) already runs it back through
    /// <see cref="Uri.UnescapeDataString(string)"/> before re-parsing (design decision #2).
    /// </summary>
    private static string BuildWikiLinkHref(LinkTarget parsed)
    {
        var notePart = parsed.Note;
        if (notePart.Length > 0 && !System.IO.Path.HasExtension(notePart))
            notePart += ".md";

        var anchorPart = parsed.Kind switch
        {
            AnchorKind.Heading => parsed.Anchor,
            AnchorKind.Block   => "^" + parsed.Anchor,
            _                  => null,
        };

        return anchorPart is null ? notePart : $"{notePart}#{anchorPart}";
    }

    private string WrapInPage(string bodyHtml, bool isDarkTheme, string? vaultRoot)
    {
        // WebView2 maps "vault.local" to the vault root folder so that
        // relative image paths work without writing temp files.
        var baseHref = vaultRoot is not null ? "http://vault.local/" : "";
        var bodyClass = isDarkTheme ? "markdown-body dark" : "markdown-body";

        // Assets aportados por plugins (ej. Mermaid), agrupados por ubicación.
        var assets    = _registry.PreviewAssets;
        var headStart = RenderAssets(assets.Where(a => a.Placement == AssetPlacement.HeadStart));
        var headEnd   = RenderAssets(assets.Where(a => a.Placement == AssetPlacement.HeadEnd));
        var bodyEnd   = RenderAssets(assets.Where(a => a.Placement == AssetPlacement.BodyEnd));

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                {{(baseHref.Length > 0 ? "<base href=\"http://vault.local/\">" : "")}}
                {{headStart}}
                <style>{{GithubCss}}</style>
                {{headEnd}}
            </head>
            <body class="{{bodyClass}}">
            <div id="mv-content">
            {{bodyHtml}}
            </div>
            {{bodyEnd}}
            <script>
                // In-place content update. The markdown lives in #mv-content; the init
                // scripts (this one + plugin scripts like Mermaid/Highlight) are its
                // SIBLINGS, so swapping the div's innerHTML leaves them intact. We then
                // re-dispatch DOMContentLoaded so those one-time initialisers re-run
                // against the fresh content — no full page reload, so scroll is preserved.
                // (Assumes init scripts register plain DOMContentLoaded listeners, not
                // {once:true}; all bundled plugins do.)
                window.__mvSetBody = function(html) {
                    var el = document.getElementById('mv-content');
                    if (!el) return;
                    el.innerHTML = html;
                    document.dispatchEvent(new Event('DOMContentLoaded'));
                };

                // Anchor navigation (design decision #10): scrolls to an already-rendered
                // heading/block id. Returns false when the id is absent so the host can report
                // the miss via StatusSink instead of silently doing nothing — a plugin that
                // restructures a block (e.g. Callouts) can drop the id between GetHeadings/
                // GetBlockMarkers' AST-level view and what actually lands in this DOM.
                window.__mvScrollToId = function(id) {
                    var el = document.getElementById(id);
                    if (!el) return false;
                    el.scrollIntoView({ block: 'start' });
                    return true;
                };

                document.addEventListener("DOMContentLoaded", function() {
                    // ── Wrap tables for horizontal scroll (comportamiento del host) ──
                    document.querySelectorAll('table').forEach(function(table) {
                        if (table.parentElement.classList.contains('table-wrapper')) return;
                        var wrapper = document.createElement('div');
                        wrapper.className = 'table-wrapper';
                        table.parentNode.insertBefore(wrapper, table);
                        wrapper.appendChild(table);
                    });
                });
            </script>
            </body>
            </html>
            """;
    }

    // ─── Inyección de PreviewAssets de plugins ───────────────────────────────

    /// <summary>Convierte una lista de <see cref="PreviewAsset"/> en etiquetas HTML.</summary>
    private static string RenderAssets(IEnumerable<PreviewAsset> assets)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var a in assets)
        {
            if (a.Source == AssetSource.Url)
            {
                sb.AppendLine(a.Kind == AssetKind.Style
                    ? $"<link rel=\"stylesheet\" href=\"{a.Value}\">"
                    : $"<script src=\"{a.Value}\"></script>");
            }
            else
            {
                // Inline o BundledFile (ya resuelto a ruta absoluta) → se embebe el contenido.
                var content = a.Source == AssetSource.BundledFile ? SafeReadFile(a.Value) : a.Value;
                sb.AppendLine(a.Kind == AssetKind.Style
                    ? $"<style>{content}</style>"
                    : $"<script>{content}</script>");
            }
        }
        return sb.ToString();
    }

    private static string SafeReadFile(string path)
    {
        try   { return System.IO.File.ReadAllText(path); }
        catch { return $"/* MarkdownVault: no se pudo leer el asset '{path}' */"; }
    }

    /// <summary>
    /// Prepares raw HTML content for preview by injecting a &lt;base&gt; tag
    /// pointing to the virtual host (http://vault.local/) so relative assets resolve.
    /// </summary>
    public string PrepareHtmlForPreview(string html, string? vaultRoot)
    {
        if (string.IsNullOrEmpty(vaultRoot)) return html;
        var baseTag = "<base href=\"http://vault.local/\">";
        if (html.Contains("<base", StringComparison.OrdinalIgnoreCase)) return html;

        // Try to insert after <head>
        int headIndex = html.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
        if (headIndex >= 0)
        {
            return html.Insert(headIndex + 6, "\n" + baseTag);
        }

        // Try to insert after <html>
        int htmlIndex = html.IndexOf("<html>", StringComparison.OrdinalIgnoreCase);
        if (htmlIndex >= 0)
        {
            return html.Insert(htmlIndex + 6, "\n<head>" + baseTag + "</head>");
        }

        // Otherwise, prepend
        return baseTag + "\n" + html;
    }

    // GitHub-flavored Markdown CSS (inlined to avoid external HTTP requests).
    private const string GithubCss = """
        *, *::before, *::after { box-sizing: border-box; }
        body.markdown-body {
          font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Helvetica, Arial, sans-serif;
          font-size: 16px; line-height: 1.6; color: #24292f;
          /* Explicit light background, symmetric with the dark rule below. Without it the
             body is transparent and leans on WebView2's DefaultBackgroundColor, which is
             stale right after a dark→light navigation → the preview "stays dark". */
          background: #ffffff;
          padding: 24px clamp(24px, 3vw, 64px);
          max-width: min(95vw, 1600px);
          margin: 0 auto;
        }
        h1,h2,h3,h4,h5,h6 { margin-top:24px; margin-bottom:16px; font-weight:600; line-height:1.25; }
        h1 { font-size:2em;   border-bottom:1px solid #d0d7de; padding-bottom:.3em; }
        h2 { font-size:1.5em; border-bottom:1px solid #d0d7de; padding-bottom:.3em; }
        h3 { font-size:1.25em; }
        a  { color:#0969da; text-decoration:none; }
        a:hover { text-decoration:underline; }
        code {
          background:#f6f8fa; padding:.2em .4em;
          border-radius:6px; font-size:85%;
          font-family: SFMono-Regular, Consolas, "Liberation Mono", Menlo, monospace;
        }
        pre {
          background:#f6f8fa; padding:16px; overflow:auto;
          border-radius:6px; line-height:1.45;
        }
        pre code { background:none; padding:0; border-radius:0; font-size:100%; }
        blockquote {
          margin:0; padding:0 1em;
          color:#57606a; border-left:.25em solid #d0d7de;
        }
        /* Table wrapper for horizontal scroll on large tables */
        .table-wrapper {
          width: 100%; overflow-x: auto; margin: 16px 0;
          -webkit-overflow-scrolling: touch;
        }
        table { border-collapse:collapse; width:100%; min-width:100%; }
        th,td { padding:6px 13px; border:1px solid #d0d7de; white-space:nowrap; }
        th { font-weight:600; }
        tr { background:#fff; border-top:1px solid #d8dee4; }
        tr:nth-child(2n) { background:#f6f8fa; }
        img { max-width:100%; height:auto; }
        hr  { height:.25em; background-color:#d0d7de; border:0; margin:24px 0; }
        ul, ol { padding-left:2em; }
        li + li { margin-top:.25em; }
        .task-list-item { list-style-type:none; }
        .task-list-item input { margin-right:.5em; }
        /* Dark-mode override injected by theme toggling */
        body.dark.markdown-body {
          color:#e6edf3; background:#0d1117;
        }
        body.dark.markdown-body h1,
        body.dark.markdown-body h2 { border-color:#30363d; }
        body.dark.markdown-body code,
        body.dark.markdown-body pre  { background:#161b22; }
        body.dark.markdown-body table th,
        body.dark.markdown-body table td { border-color:#30363d; }
        body.dark.markdown-body tr     { background:#0d1117; }
        body.dark.markdown-body tr:nth-child(2n) { background:#161b22; }
        body.dark.markdown-body blockquote { color:#8b949e; border-color:#30363d; }
        body.dark.markdown-body a { color:#58a6ff; }
        body.dark.markdown-body hr { background-color:#30363d; }
        /* Scrollbar styling for table wrappers */
        .table-wrapper::-webkit-scrollbar { height: 6px; }
        .table-wrapper::-webkit-scrollbar-track { background: transparent; }
        .table-wrapper::-webkit-scrollbar-thumb { background: #d0d7de; border-radius: 3px; }
        body.dark .table-wrapper::-webkit-scrollbar-thumb { background: #30363d; }
        """;
}
