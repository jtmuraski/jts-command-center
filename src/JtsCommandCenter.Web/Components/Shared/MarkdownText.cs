using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace JtsCommandCenter.Web.Components.Shared;

/// <summary>
/// Renders user-written Markdown (descriptions, notes) to HTML that is safe to show as-is: raw HTML is
/// escaped, and links or images whose URL could run script are neutralized.
/// </summary>
public static class MarkdownText
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseAutoLinks()
        .UsePipeTables()
        .UseTaskLists()
        .UseEmphasisExtras()
        // Notes are typed like plain text, so a single line break should show as one.
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    public static string ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return "";
        }

        var document = Markdig.Markdown.Parse(markdown, Pipeline);
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (!IsSafeUrl(link.Url, allowMail: !link.IsImage))
            {
                link.Url = "#";
            }

            if (!link.IsImage)
            {
                OpenInNewTab(link.GetAttributes());
            }
        }

        foreach (var link in document.Descendants<AutolinkInline>())
        {
            if (!IsSafeUrl(link.Url, allowMail: true))
            {
                link.Url = "#";
            }

            OpenInNewTab(link.GetAttributes());
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }

    /// <summary>Relative URLs and http(s) are allowed (and mailto for links); any other scheme is not.</summary>
    internal static bool IsSafeUrl(string? url, bool allowMail)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        // Browsers ignore whitespace and control characters inside a scheme ("java\tscript:").
        var compact = new string(url.Where(c => c > ' ').ToArray());
        var colon = compact.IndexOf(':');
        var firstDelimiter = compact.IndexOfAny(['/', '?', '#']);
        if (colon < 0 || (firstDelimiter >= 0 && firstDelimiter < colon))
        {
            return true; // relative
        }

        var scheme = compact[..colon];
        return scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
            || scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
            || (allowMail && scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase));
    }

    private static void OpenInNewTab(HtmlAttributes attributes)
    {
        attributes.AddPropertyIfNotExist("target", "_blank");
        attributes.AddPropertyIfNotExist("rel", "noopener noreferrer");
    }
}
