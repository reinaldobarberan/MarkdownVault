using MarkdownVault.Services;
using MarkdownVault.Services.Plugins;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Covers the body/shell split that powers in-place preview updates: RenderBody must
/// return only the rendered fragment (no page shell), and it must be exactly the body
/// that RenderToHtml embeds — otherwise a DOM patch would diverge from a full reload.
/// </summary>
public class MarkdownServiceRenderBodyTests
{
    private static MarkdownService NewService() => new(new PluginRegistry());

    [Fact]
    public void RenderBody_returns_fragment_without_page_shell()
    {
        var body = NewService().RenderBody("# Title\n\nSome **text**.");

        Assert.Contains("<h1", body);
        Assert.Contains("<strong>text</strong>", body);
        Assert.DoesNotContain("<!DOCTYPE", body);
        Assert.DoesNotContain("<html", body);
        Assert.DoesNotContain("<body", body);
    }

    [Fact]
    public void RenderToHtml_embeds_exactly_the_RenderBody_fragment()
    {
        var svc      = NewService();
        const string md = "## Heading\n\n- a\n- b\n";

        var body = svc.RenderBody(md);
        var page = svc.RenderToHtml(md, isDarkTheme: false);

        Assert.Contains(body, page);            // the full page contains the fragment verbatim
        Assert.Contains("id=\"mv-content\"", page);   // fragment lives in the patch container
        Assert.Contains("__mvSetBody", page);         // and the page exposes the patch hook
    }

    [Fact]
    public void RenderToHtml_marks_dark_body_class_when_requested()
    {
        var page = NewService().RenderToHtml("# x", isDarkTheme: true);

        Assert.Contains("markdown-body dark", page);
    }

    [Fact]
    public void RenderToHtml_light_page_does_not_use_dark_body_class()
    {
        var page = NewService().RenderToHtml("# x", isDarkTheme: false);

        Assert.DoesNotContain("markdown-body dark", page);
    }

    // Regression: the light body must carry its OWN explicit background. It used to be
    // transparent and lean on WebView2's DefaultBackgroundColor, which is stale right
    // after a dark→light navigation → the preview "stayed dark". The rendered document
    // must be self-contained so the visible background never depends on host paint timing.
    [Fact]
    public void RenderToHtml_light_body_has_explicit_white_background()
    {
        var page = NewService().RenderToHtml("# x", isDarkTheme: false);

        Assert.Contains("body.markdown-body", page);
        Assert.Contains("background: #ffffff", page);
    }

    [Fact]
    public void RenderToHtml_dark_body_keeps_its_explicit_dark_background()
    {
        var page = NewService().RenderToHtml("# x", isDarkTheme: true);

        // Dark override must remain (higher specificity than the light rule), otherwise
        // the new light background would bleed through in dark mode.
        Assert.Contains("background:#0d1117", page);
    }
}
