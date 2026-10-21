using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using JtsCommandCenter.Core.Sports;
using static JtsCommandCenter.Infrastructure.Sports.EspnJson;

namespace JtsCommandCenter.Infrastructure.Sports;

/// <summary>
/// League and team news merged from ESPN's news API and plain RSS 2.0 / Atom feeds. ESPN's team filter on
/// its news endpoint is ignored by the API, so team news is ESPN articles tagged with the team plus the
/// team's own feeds plus league-feed headlines that mention the team.
/// </summary>
public sealed partial class SportsNewsService(
    HttpClient http,
    IMemoryCache cache,
    IOptions<SportsCenterOptions> options,
    ILogger<SportsNewsService> logger) : ISportsNewsService
{
    private const string EspnSource = "ESPN";
    private const int SummaryLength = 220;

    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    private readonly SportsCenterOptions settings = options.Value;

    public async Task<LeagueNews> GetNewsAsync(string league, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"sports-news:{league}";
        if (cache.TryGetValue(cacheKey, out LeagueNews? cached) && cached is not null)
        {
            return cached;
        }

        if (!settings.Leagues.TryGetValue(league, out var leagueOptions))
        {
            throw new InvalidOperationException($"No SportsCenter settings for league '{league}'.");
        }

        var focus = leagueOptions.FocusTeam;
        var failed = new List<string>();

        var espnTask = FetchEspnAsync(leagueOptions, failed, cancellationToken);
        var leagueFeedTasks = leagueOptions.Feeds.Select(f => FetchFeedAsync(f, failed, cancellationToken)).ToList();
        var teamFeedTasks = (focus?.Feeds ?? []).Select(f => FetchFeedAsync(f, failed, cancellationToken)).ToList();

        var espn = await espnTask;
        var leagueFeeds = (await Task.WhenAll(leagueFeedTasks)).SelectMany(a => a).ToList();
        var teamFeeds = (await Task.WhenAll(teamFeedTasks)).SelectMany(a => a).ToList();

        if (espn.Count == 0 && leagueFeeds.Count == 0 && teamFeeds.Count == 0 && failed.Count > 0)
        {
            throw new InvalidOperationException("No news sources could be loaded.");
        }

        var headlines = Merge(
            espn.Select(e => e.Article).Concat(leagueFeeds),
            leagueOptions.ExcludeKeywords,
            leagueOptions.HeadlineCount);

        var teamNews = focus is null
            ? []
            : Merge(
                teamFeeds
                    .Concat(espn.Where(e => IsAboutTeam(e, focus)).Select(e => e.Article))
                    .Concat(leagueFeeds.Where(a => MentionsAny(a.Title, focus.Keywords))),
                leagueOptions.ExcludeKeywords,
                focus.ArticleCount);

        var news = new LeagueNews(leagueOptions.Name, headlines, focus?.Name, teamNews, failed, DateTimeOffset.UtcNow);

        // A partial result is retried sooner so a briefly-down feed comes back quickly.
        var ttl = failed.Count > 0 ? TimeSpan.FromMinutes(2) : TimeSpan.FromMinutes(settings.NewsCacheMinutes);
        cache.Set(cacheKey, news, ttl);
        return news;
    }

    /// <summary>Drops excluded and duplicate headlines (same title from two outlets), newest first.</summary>
    private static List<NewsArticle> Merge(IEnumerable<NewsArticle> articles, List<string> exclude, int count) =>
        articles
            .Where(a => !MentionsAny(a.Title, exclude) && !MentionsAny(a.Summary, exclude))
            .DistinctBy(a => NormalizeTitle(a.Title))
            .OrderByDescending(a => a.Published)
            .Take(count)
            .ToList();

    /// <summary>
    /// ESPN tags league-wide roundups (power rankings, picks) with many teams, so a team tag only counts when
    /// the story is about a couple of teams at most or names the team in its headline or teaser.
    /// </summary>
    private static bool IsAboutTeam(EspnArticle article, FocusTeamOptions focus) =>
        article.TeamIds.Contains(focus.EspnTeamId)
        && (article.TeamIds.Count <= 2 || MentionsAny(article.Article.Title, focus.Keywords) || MentionsAny(article.Article.Summary, focus.Keywords));

    private static bool MentionsAny(string? text, List<string> words) =>
        text is not null && words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeTitle(string title) => NonWordRegex().Replace(title.ToLowerInvariant(), "");

    private sealed record EspnArticle(NewsArticle Article, HashSet<string> TeamIds);

    private async Task<List<EspnArticle>> FetchEspnAsync(LeagueOptions league, List<string> failed, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = await http.GetJsonAsync($"apis/site/v2/sports/{league.Sport}/{league.League}/news?limit=50", cancellationToken);
            var articles = new List<EspnArticle>();

            foreach (var a in Array(doc.RootElement, "articles"))
            {
                // "Media" entries are video clips (mostly fantasy previews); keep written stories.
                if (Str(a, "type") == "Media")
                {
                    continue;
                }

                var title = Str(a, "headline");
                var url = a.TryGetProperty("links", out var links) && links.TryGetProperty("web", out var web) ? Str(web, "href") : null;
                if (title is null || url is null || !DateTimeOffset.TryParse(Str(a, "published"), CultureInfo.InvariantCulture, out var published))
                {
                    continue;
                }

                var teamIds = Array(a, "categories")
                    .Where(c => Str(c, "type") == "team")
                    .Select(c => Int(c, "teamId")?.ToString(CultureInfo.InvariantCulture))
                    .OfType<string>()
                    .ToHashSet();

                articles.Add(new EspnArticle(new NewsArticle(title, url, EspnSource, published, Trim(Str(a, "description"))), teamIds));
            }

            return articles;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to load ESPN {League} news", league.League);
            lock (failed) failed.Add(EspnSource);
            return [];
        }
    }

    private async Task<List<NewsArticle>> FetchFeedAsync(NewsFeedOptions feed, List<string> failed, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await http.GetStreamAsync(feed.Url, cancellationToken);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, Async = true });
            var doc = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);

            var items = doc.Descendants("item").Select(i => ParseRssItem(i, feed.Name))
                .Concat(doc.Descendants(Atom + "entry").Select(e => ParseAtomEntry(e, feed.Name)));

            return items.OfType<NewsArticle>().ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or XmlException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to load news feed {Feed}", feed.Name);
            lock (failed) failed.Add(feed.Name);
            return [];
        }
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

    /// <summary>Cleans a teaser and shortens it to a sentence-ish length.</summary>
    private static string? Trim(string? html)
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

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonWordRegex();

    [GeneratedRegex(@"([+-]\d{2})(\d{2})$")]
    private static partial Regex Rfc822OffsetRegex();
}
