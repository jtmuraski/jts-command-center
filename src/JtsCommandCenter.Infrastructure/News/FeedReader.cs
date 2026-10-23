using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using JtsCommandCenter.Core.News;

namespace JtsCommandCenter.Infrastructure.News;

/// <summary>Reads RSS 2.0 and Atom feeds into <see cref="NewsArticle"/>s, plus the text helpers news merging needs.</summary>
internal static partial class FeedReader
{
    private const int SummaryLength = 220;

    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    /// <summary>
    /// Fetches and parses one feed. Items without a title, link or parseable date are skipped. Throws
    /// <see cref="HttpRequestException"/>, <see cref="XmlException"/> or <see cref="TaskCanceledException"/>
    /// when the feed can't be loaded.
    /// </summary>
    public static async Task<List<NewsArticle>> ReadAsync(HttpClient http, NewsFeedOptions feed, CancellationToken cancellationToken)
    {
        await using var stream = await http.GetStreamAsync(feed.Url, cancellationToken);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, Async = true });
        var doc = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);

        var items = doc.Descendants("item").Select(i => ParseRssItem(i, feed.Name))
            .Concat(doc.Descendants(Atom + "entry").Select(e => ParseAtomEntry(e, feed.Name)));

        return items.OfType<NewsArticle>().ToList();
    }

    /// <summary>Drops excluded and duplicate headlines (same title from two outlets), newest first.</summary>
    public static List<NewsArticle> Merge(IEnumerable<NewsArticle> articles, IReadOnlyCollection<string> exclude, int count) =>
        articles
            .Where(a => !MentionsAny(a.Title, exclude) && !MentionsAny(a.Summary, exclude))
            .DistinctBy(a => NormalizeTitle(a.Title))
            .OrderByDescending(a => a.Published)
            .Take(count)
            .ToList();

    public static bool MentionsAny(string? text, IEnumerable<string> words) =>
        text is not null && words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>Cleans a teaser and shortens it to a sentence-ish length.</summary>
    public static string? Trim(string? html)
    {
        var text = Clean(html);
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Length <= SummaryLength)
        {
            return text;
        }

        var cut = text.LastIndexOf(' ', SummaryLength);
        return text[..(cut > 0 ? cut : SummaryLength)].TrimEnd(',', '.', ';', ':') + "…";
    }

    private static NewsArticle? ParseRssItem(XElement item, string source)
    {
        var title = Clean(item.Element("title")?.Value);
        var link = item.Element("link")?.Value.Trim();
        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(link) || ParseDate(item.Element("pubDate")?.Value) is not DateTimeOffset published)
        {
            return null;
        }

        return new NewsArticle(title, link, source, published, Trim(item.Element("description")?.Value));
    }

    private static NewsArticle? ParseAtomEntry(XElement entry, string source)
    {
        var title = Clean(entry.Element(Atom + "title")?.Value);
        var link = entry.Elements(Atom + "link")
            .FirstOrDefault(l => (string?)l.Attribute("rel") is null or "alternate")?.Attribute("href")?.Value;
        var date = entry.Element(Atom + "published")?.Value ?? entry.Element(Atom + "updated")?.Value;
        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(link) || ParseDate(date) is not DateTimeOffset published)
        {
            return null;
        }

        var summary = entry.Element(Atom + "summary")?.Value ?? entry.Element(Atom + "content")?.Value;
        return new NewsArticle(title, link, source, published, Trim(summary));
    }

    /// <summary>Parses RFC 822 (RSS) and ISO 8601 (Atom) dates, including RFC 822's "+0000" style offsets.</summary>
    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        // "Wed, 21 Oct 2026 19:14:30 +0000" → "... +00:00", which .NET understands.
        var withColon = Rfc822OffsetRegex().Replace(value, "$1:$2");
        return DateTimeOffset.TryParse(withColon, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed)
            ? parsed
            : null;
    }

    /// <summary>Strips HTML and collapses whitespace.</summary>
    private static string Clean(string? html) =>
        html is null ? "" : WhitespaceRegex().Replace(WebUtility.HtmlDecode(TagRegex().Replace(html, " ")), " ").Trim();

    private static string NormalizeTitle(string title) => NonWordRegex().Replace(title.ToLowerInvariant(), "");

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonWordRegex();

    [GeneratedRegex(@"([+-]\d{2})(\d{2})$")]
    private static partial Regex Rfc822OffsetRegex();
}
