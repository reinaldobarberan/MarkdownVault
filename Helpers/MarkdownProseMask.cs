using System.Text;
using System.Text.RegularExpressions;

namespace MarkdownVault.Helpers;

/// <summary>
/// Blanks out non-prose regions of a Markdown line (inline code, URLs, HTML tags,
/// link/wikilink targets) so a spell checker only sees actual prose. Replacements
/// keep the original length, so error offsets map straight back onto the source line.
/// </summary>
public static class MarkdownProseMask
{
    private static readonly Regex InlineCodeRx = new(@"`[^`]*`",        RegexOptions.Compiled);
    private static readonly Regex BareUrlRx    = new(@"https?://\S+",   RegexOptions.Compiled);
    private static readonly Regex HtmlTagRx    = new(@"<[^>]+>",        RegexOptions.Compiled);
    private static readonly Regex LinkTargetRx = new(@"\]\(([^)]*)\)",  RegexOptions.Compiled); // URL inside [text](url)
    private static readonly Regex WikiTargetRx = new(@"\[\[([^\]]+)\]\]", RegexOptions.Compiled);

    public static string Mask(string line)
    {
        if (string.IsNullOrEmpty(line)) return line;

        var sb = new StringBuilder(line);
        Blank(sb, line, InlineCodeRx, wholeMatch: true);
        Blank(sb, line, BareUrlRx,    wholeMatch: true);
        Blank(sb, line, HtmlTagRx,    wholeMatch: true);
        Blank(sb, line, LinkTargetRx, wholeMatch: false); // keep link text, blank the URL
        Blank(sb, line, WikiTargetRx, wholeMatch: false);
        return sb.ToString();
    }

    private static void Blank(StringBuilder sb, string source, Regex regex, bool wholeMatch)
    {
        foreach (Match m in regex.Matches(source))
        {
            Capture capture;
            if (wholeMatch)
            {
                capture = m;
            }
            else
            {
                var group = m.Groups[1];
                if (!group.Success) continue;
                capture = group;
            }

            for (int i = capture.Index; i < capture.Index + capture.Length; i++)
                sb[i] = ' ';
        }
    }
}
