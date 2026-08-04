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
}
