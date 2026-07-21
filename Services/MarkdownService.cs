using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;

namespace MarkdownVault.Services;

/// <summary>Converts Markdown text to a self-contained HTML page using Markdig.</summary>
public class MarkdownService
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .Build();

    /// <summary>
    /// Renders <paramref name="markdown"/> to a full HTML document.
    /// When <paramref name="vaultRoot"/> is provided the virtual host base URL
    /// (<c>http://vault.local/</c>) is injected so that relative images resolve.
    /// </summary>
    public string RenderToHtml(string markdown, bool isDarkTheme, string? vaultRoot = null)
    {
        var processed = PreprocessWikiLinks(markdown);
        var body = Markdig.Markdown.ToHtml(processed, Pipeline);
        return WrapInPage(body, isDarkTheme, vaultRoot);
    }

    // ─── Wikilink preprocessing ──────────────────────────────────────────────

    // Fenced (```...```) and inline (`...`) code spans. Wikilink rewriting must skip
    // these so code stays intact — notably Mermaid's [[subroutine]] node shape, which
    // would otherwise be mangled into a Markdown link and break the diagram.
    private static readonly Regex CodeSpanRegex = new(
        @"```[\s\S]*?```|`[^`]*`", RegexOptions.Compiled);

    // [[target]] or [[target|display]]
    private static readonly Regex WikiLinkRegex = new(
        @"\[\[([^\]|]+)(?:\|([^\]]+))?\]\]", RegexOptions.Compiled);

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

    private static string ConvertWikiLinks(string text) =>
        WikiLinkRegex.Replace(text, match =>
        {
            var target  = match.Groups[1].Value.Trim();
            // Default display is the note name (not the full path) for clean link text.
            var display = match.Groups[2].Success
                ? match.Groups[2].Value.Trim()
                : System.IO.Path.GetFileNameWithoutExtension(target);

            // Add .md if the target has no extension
            var href = System.IO.Path.HasExtension(target) ? target : target + ".md";
            // CommonMark: link destinations that contain spaces or parentheses must
            // be wrapped in angle brackets, otherwise the link isn't recognized.
            if (href.IndexOfAny([' ', '(', ')']) >= 0)
                href = $"<{href}>";
            return $"[{display}]({href})";
        });

    private static string WrapInPage(string bodyHtml, bool isDarkTheme, string? vaultRoot)
    {
        // WebView2 maps "vault.local" to the vault root folder so that
        // relative image paths work without writing temp files.
        var baseHref = vaultRoot is not null ? "http://vault.local/" : "";
        var bodyClass = isDarkTheme ? "markdown-body dark" : "markdown-body";

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                {{(baseHref.Length > 0 ? "<base href=\"http://vault.local/\">" : "")}}
                <style>{{GithubCss}}</style>
                <script src="https://cdn.jsdelivr.net/npm/mermaid@11.15.0/dist/mermaid.min.js"></script>
            </head>
            <body class="{{bodyClass}}">
            {{bodyHtml}}
            <script>
                document.addEventListener("DOMContentLoaded", function() {
                    // ── Mermaid rendering ──
                    var elements = document.querySelectorAll('pre code.language-mermaid');
                    if (elements.length > 0) {
                        var isDark = document.body.classList.contains('dark');
                        mermaid.initialize({
                            startOnLoad: false,
                            theme: isDark ? 'dark' : 'default',
                            securityLevel: 'loose'
                        });
                        
                        elements.forEach(function(el) {
                            var code = el.textContent;
                            var pre = el.parentElement;
                            var div = document.createElement('div');
                            div.className = 'mermaid';
                            div.textContent = code;
                            pre.parentNode.replaceChild(div, pre);
                        });
                        
                        mermaid.run({ nodes: document.querySelectorAll('.mermaid') });
                    }

                    // ── Wrap tables for horizontal scroll ──
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
