using JtsCommandCenter.Web.Components.Shared;

namespace JtsCommandCenter.Tests.Web;

public class MarkdownTextTests
{
    [Fact]
    public void RendersMarkdown()
    {
        var html = MarkdownText.ToHtml("**Engine** updates\n\n- model\n- workflow");

        Assert.Contains("<strong>Engine</strong>", html);
        Assert.Contains("<li>model</li>", html);
    }

    [Fact]
    public void SingleLineBreak_IsKept() =>
        Assert.Contains("<br", MarkdownText.ToHtml("RFI submitted.\nWaiting for clarification."));

    [Fact]
    public void Blank_IsEmpty() => Assert.Equal("", MarkdownText.ToHtml("  "));

    [Fact]
    public void RawHtml_IsEscaped()
    {
        var html = MarkdownText.ToHtml("<script>alert(1)</script> <img src=x onerror=alert(1)>");

        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Theory]
    [InlineData("[click](javascript:alert(1))")]
    [InlineData("[click](JavaScript:alert(1))")]
    [InlineData("[click](jav&#x61;script:alert(1))")]
    [InlineData("[click](vbscript:msgbox)")]
    [InlineData("[click](data:text/html;base64,PHNjcmlwdD4=)")]
    [InlineData("<javascript:alert(1)>")]
    public void ScriptLinks_AreNeutralized(string markdown)
    {
        var html = MarkdownText.ToHtml(markdown);

        Assert.DoesNotContain("script:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScriptImages_AreNeutralized() =>
        Assert.DoesNotContain("javascript", MarkdownText.ToHtml("![x](javascript:alert(1))"));

    [Fact]
    public void WebLinks_OpenInANewTab()
    {
        var html = MarkdownText.ToHtml("[Ticket](https://dev.azure.com/org/_workitems/edit/42) and https://example.com");

        Assert.Contains("href=\"https://dev.azure.com/org/_workitems/edit/42\"", html);
        Assert.Contains("href=\"https://example.com\"", html);
        Assert.Contains("target=\"_blank\"", html);
        Assert.Contains("rel=\"noopener noreferrer\"", html);
    }

    [Theory]
    [InlineData("/attachments/12", true, true)]
    [InlineData("notes/readme", true, true)]
    [InlineData("#section", true, true)]
    [InlineData("https://example.com/a:b", true, true)]
    [InlineData("mailto:me@example.com", true, true)]
    [InlineData("mailto:me@example.com", false, false)]
    [InlineData("java\tscript:alert(1)", true, false)]
    [InlineData("", true, false)]
    public void IsSafeUrl_AllowsRelativeAndWebUrls(string url, bool allowMail, bool expected) =>
        Assert.Equal(expected, MarkdownText.IsSafeUrl(url, allowMail));
}
