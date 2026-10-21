using JtsCommandCenter.Core.Sports;
using JtsCommandCenter.Infrastructure.Sports;
using JtsCommandCenter.Tests.Support;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JtsCommandCenter.Tests.Sports;

public class SportsNewsServiceTests
{
    private const string EspnNews = "football/nfl/news";
    private const string LeagueFeedUrl = "https://feeds.example.test/nfl.xml";
    private const string TeamFeedUrl = "https://team.example.test/news.xml";
    private const string AtomFeedUrl = "https://blog.example.test/atom.xml";

    private static LeagueOptions League() => new()
    {
        Name = "NFL",
        Sport = "football",
        League = "nfl",
        HeadlineCount = 20,
        Feeds = [new() { Name = "League Feed", Url = LeagueFeedUrl }],
        ExcludeKeywords = ["promo code"],
        FocusTeam = new FocusTeamOptions
        {
            Name = "Vikings",
            EspnTeamId = "16",
            Keywords = ["Vikings"],
            ArticleCount = 12,
            Feeds =
            [
                new() { Name = "Team Site", Url = TeamFeedUrl },
                new() { Name = "Team Blog", Url = AtomFeedUrl },
            ]
        }
    };

    private static SportsNewsService CreateService(FakeHttpHandler handler, LeagueOptions? league = null)
    {
        var options = new SportsCenterOptions();
        options.Leagues["nfl"] = league ?? League();

        return new SportsNewsService(
            handler.CreateClient(),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(options),
            NullLogger<SportsNewsService>.Instance);
    }

    private static string EspnArticle(string headline, string published, string type = "Story", params int[] teamIds) =>
        $$"""
        {
          "type": "{{type}}",
          "headline": "{{headline}}",
          "description": "About {{headline}}",
          "published": "{{published}}",
          "links": { "web": { "href": "https://www.espn.com/{{headline.GetHashCode()}}" } },
          "categories": [ {{string.Join(",", teamIds.Select(id => $$"""{ "type": "team", "teamId": {{id}} }"""))}} ]
        }
        """;

    private static string EspnFeed(params string[] articles) => $$"""{ "articles": [ {{string.Join(",", articles)}} ] }""";

    private static string Rss(params (string Title, string PubDate)[] items) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0"><channel><title>Feed</title>
        {string.Concat(items.Select(i => $"<item><title>{i.Title}</title><link>https://example.test/{Uri.EscapeDataString(i.Title)}</link><description><![CDATA[<p>Teaser for <b>{i.Title}</b></p>]]></description><pubDate>{i.PubDate}</pubDate></item>"))}
        </channel></rss>
        """;

    private static string Atom(params (string Title, string Published)[] entries) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom"><title>Blog</title>
        {string.Concat(entries.Select(e => $"<entry><title type=\"html\">{e.Title}</title><link rel=\"alternate\" type=\"text/html\" href=\"https://blog.example.test/{Uri.EscapeDataString(e.Title)}\" /><published>{e.Published}</published><summary type=\"html\">Summary</summary></entry>"))}
        </feed>
        """;

    private static FakeHttpHandler StandardFeeds() => new FakeHttpHandler()
        .Json(EspnNews, EspnFeed(
            EspnArticle("Bears RB ruled out", "2026-10-21T20:00:00Z", teamIds: 3),
            EspnArticle("Vikings sign a kicker", "2026-10-21T18:00:00Z", teamIds: 16),
            EspnArticle("Power Rankings for Week 7", "2026-10-21T17:00:00Z", "Story", 16, 3, 8, 9, 22),
            EspnArticle("Fantasy preview clip", "2026-10-21T19:30:00Z", "Media")))
        .Xml(LeagueFeedUrl, Rss(
            ("Use promo code WIN to get a bonus", "Wed, 21 Oct 2026 19:59:00 +0000"),
            ("Bears RB ruled out!", "Wed, 21 Oct 2026 19:50:00 +0000"),
            ("Vikings defense is the best in the league", "Wed, 21 Oct 2026 19:00:00 -0400"),
            ("Patriots set starting lineup", "Tue, 20 Oct 2026 12:00:00 GMT")))
        .Xml(TeamFeedUrl, Rss(("How to watch Vikings vs. Colts", "Wed, 21 Oct 2026 18:38:41 GMT")))
        .Xml(AtomFeedUrl, Atom(("News and Links: Trade talk", "2026-10-21T15:45:02-04:00")));

    [Fact]
    public async Task Headlines_MergeSourcesNewestFirst_DedupeAndExclude()
    {
        var news = await CreateService(StandardFeeds()).GetNewsAsync("nfl");

        var titles = news.Headlines.Select(a => a.Title).ToList();

        // ESPN's "Bears RB ruled out" and the feed's "Bears RB ruled out!" are the same story.
        Assert.Single(titles, t => t.StartsWith("Bears RB ruled out"));
        Assert.DoesNotContain(titles, t => t.Contains("promo code"));
        Assert.DoesNotContain("Fantasy preview clip", titles); // ESPN video clips are skipped
        Assert.Equal(news.Headlines.OrderByDescending(a => a.Published).Select(a => a.Title), titles);
        Assert.Equal("NFL", news.LeagueName);
        Assert.Empty(news.FailedSources);
    }

    [Fact]
    public async Task TeamNews_CombinesTeamFeedsTaggedEspnAndKeywordMatches()
    {
        var news = await CreateService(StandardFeeds()).GetNewsAsync("nfl");

        var titles = news.TeamNews.Select(a => a.Title).ToList();

        Assert.Equal("Vikings", news.TeamName);
        Assert.Contains("How to watch Vikings vs. Colts", titles);           // team feed (RSS)
        Assert.Contains("News and Links: Trade talk", titles);               // team feed (Atom)
        Assert.Contains("Vikings sign a kicker", titles);                    // ESPN, tagged with the team
        Assert.Contains("Vikings defense is the best in the league", titles); // league feed, names the team
        Assert.DoesNotContain("Power Rankings for Week 7", titles);          // ESPN roundup tagged with 5 teams
        Assert.DoesNotContain(titles, t => t.StartsWith("Bears"));
    }

    [Fact]
    public async Task FeedParsing_HandlesRfc822OffsetsAndStripsHtml()
    {
        var news = await CreateService(StandardFeeds()).GetNewsAsync("nfl");

        var vikingsDefense = news.Headlines.Single(a => a.Title.StartsWith("Vikings defense"));
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 23, 0, 0, TimeSpan.Zero), vikingsDefense.Published.ToUniversalTime());
        Assert.Equal("Teaser for Vikings defense is the best in the league", vikingsDefense.Summary);
        Assert.Equal("League Feed", vikingsDefense.Source);

        var atom = news.TeamNews.Single(a => a.Source == "Team Blog");
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 19, 45, 2, TimeSpan.Zero), atom.Published.ToUniversalTime());
        Assert.StartsWith("https://blog.example.test/", atom.Url);
    }

    [Fact]
    public async Task FailedFeed_IsReported_OthersStillShow()
    {
        var handler = new FakeHttpHandler()
            .Fail(LeagueFeedUrl)
            .Json(EspnNews, EspnFeed(EspnArticle("Vikings sign a kicker", "2026-10-21T18:00:00Z", teamIds: 16)))
            .Xml(TeamFeedUrl, Rss(("How to watch Vikings vs. Colts", "Wed, 21 Oct 2026 18:38:41 GMT")))
            .Xml(AtomFeedUrl, Atom(("News and Links", "2026-10-21T15:45:02-04:00")));

        var news = await CreateService(handler).GetNewsAsync("nfl");

        Assert.Equal(["League Feed"], news.FailedSources);
        Assert.Single(news.Headlines);
        Assert.Equal(3, news.TeamNews.Count);
    }

    [Fact]
    public async Task MalformedFeed_IsReportedAsFailed()
    {
        var service = CreateService(new FakeHttpHandler()
            .Xml(LeagueFeedUrl, "<rss><channel><item><title>Broken")
            .Json(EspnNews, EspnFeed(EspnArticle("Bears RB ruled out", "2026-10-21T20:00:00Z", teamIds: 3))));

        var news = await service.GetNewsAsync("nfl");

        Assert.Contains("League Feed", news.FailedSources);
        Assert.Equal("Bears RB ruled out", Assert.Single(news.Headlines).Title);
    }

    [Fact]
    public async Task EverySourceFails_Throws()
    {
        var handler = new FakeHttpHandler().Fail("example.test").Fail(EspnNews);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(handler).GetNewsAsync("nfl"));
    }

    [Fact]
    public async Task HeadlineCount_LimitsResults()
    {
        var league = League();
        league.HeadlineCount = 2;

        var news = await CreateService(StandardFeeds(), league).GetNewsAsync("nfl");

        Assert.Equal(2, news.Headlines.Count);
    }

    [Fact]
    public async Task UnknownLeague_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(StandardFeeds()).GetNewsAsync("xfl"));
    }
}
