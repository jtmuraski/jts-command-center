using JtsCommandCenter.Core.News;
using JtsCommandCenter.Infrastructure.News;
using JtsCommandCenter.Tests.Support;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace JtsCommandCenter.Tests.News;

public class NewsServiceTests
{
    private const string LocalUrl = "https://local.example.test/rss";
    private const string TvUrl = "https://tv.example.test/rss";
    private const string MilitaryUrl = "https://military.example.test/atom";
    private const string VaUrl = "https://va.example.test/rss";

    private static readonly DateTimeOffset Now = new(2026, 10, 23, 15, 0, 0, TimeSpan.Zero);

    private static NewsOptions Settings() => new()
    {
        ArticlesPerCategory = 10,
        MaxAgeDays = 7,
        ExcludeKeywords = ["coupon"],
        Categories =
        [
            new()
            {
                Key = "minnesota",
                Name = "Minnesota",
                Feeds = [new() { Name = "Local Paper", Url = LocalUrl }, new() { Name = "Local TV", Url = TvUrl }],
                ExcludeKeywords = ["sponsored"]
            },
            new()
            {
                Key = "military",
                Name = "Military",
                Feeds = [new() { Name = "Military Blog", Url = MilitaryUrl }, new() { Name = "VA", Url = VaUrl }]
            }
        ]
    };

    private static NewsService CreateService(FakeHttpHandler handler, NewsOptions? settings = null) => new(
        handler.CreateClient(),
        new MemoryCache(new MemoryCacheOptions()),
        Microsoft.Extensions.Options.Options.Create(settings ?? Settings()),
        new FakeTimeProvider(Now),
        NullLogger<NewsService>.Instance);

    private static string Rss(params (string Title, string PubDate)[] items) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0"><channel><title>Feed</title>
        {string.Concat(items.Select(i => $"<item><title><![CDATA[{i.Title}]]></title><link>https://example.test/{Uri.EscapeDataString(i.Title)}</link><description><![CDATA[<p>About <b>{i.Title}</b></p>]]></description><pubDate>{i.PubDate}</pubDate></item>"))}
        </channel></rss>
        """;

    private static string Atom(params (string Title, string Updated)[] entries) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom"><title>Blog</title>
        {string.Concat(entries.Select(e => $"<entry><title type=\"html\">{e.Title}</title><link href=\"https://blog.example.test/{Uri.EscapeDataString(e.Title)}\" /><updated>{e.Updated}</updated></entry>"))}
        </feed>
        """;

    private static FakeHttpHandler StandardFeeds() => new FakeHttpHandler()
        .Xml(LocalUrl, Rss(
            ("Snow emergency declared in Minneapolis", "Fri, 23 Oct 2026 14:00:00 +0000"),
            ("Light rail line opens", "Thu, 22 Oct 2026 09:00:00 -0500"),
            ("Sponsored: find your dream home", "Fri, 23 Oct 2026 13:00:00 +0000"),
            ("Save with this coupon", "Fri, 23 Oct 2026 12:00:00 +0000"),
            ("Old county fair recap", "Mon, 05 Oct 2026 12:00:00 +0000")))
        .Xml(TvUrl, Rss(("Snow Emergency Declared in Minneapolis!", "Fri, 23 Oct 2026 14:05:00 +0000")))
        .Xml(MilitaryUrl, Atom(("Army updates fitness test", "2026-10-23T10:00:00Z")))
        .Xml(VaUrl, Rss(("VA expands clinic hours", "Wed, 21 Oct 2026 16:00:00 GMT")));

    [Fact]
    public async Task Categories_KeepConfiguredOrderAndNames()
    {
        var report = await CreateService(StandardFeeds()).GetNewsAsync();

        Assert.Equal(["minnesota", "military"], report.Categories.Select(c => c.Key));
        Assert.Equal("Minnesota", report.Categories[0].Name);
        Assert.Equal(Now, report.FetchedAt);
    }

    [Fact]
    public async Task Articles_MergeFeedsNewestFirst_DedupeExcludeAndDropOld()
    {
        var report = await CreateService(StandardFeeds()).GetNewsAsync();

        var articles = report.Categories[0].Articles;
        var titles = articles.Select(a => a.Title).ToList();

        // The same story from two outlets shows once; global and category exclusions apply; old items drop.
        Assert.Single(titles, t => t.StartsWith("Snow emergency", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(titles, t => t.Contains("coupon"));
        Assert.DoesNotContain(titles, t => t.StartsWith("Sponsored"));
        Assert.DoesNotContain("Old county fair recap", titles);
        Assert.Equal(articles.OrderByDescending(a => a.Published), articles);
        Assert.Equal(2, titles.Count);
    }

    [Fact]
    public async Task CategoryExclusions_DontApplyToOtherCategories()
    {
        var settings = Settings();
        settings.Categories[1].Feeds = [new() { Name = "Military Blog", Url = MilitaryUrl }];
        var handler = new FakeHttpHandler()
            .Xml(LocalUrl, Rss())
            .Xml(TvUrl, Rss())
            .Xml(MilitaryUrl, Atom(("Sponsored: Veterans Day events", "2026-10-23T10:00:00Z")));

        var report = await CreateService(handler, settings).GetNewsAsync();

        Assert.Single(report.Categories[1].Articles);
    }

    [Fact]
    public async Task Parsing_ReadsRssAndAtomWithSourceAndSummary()
    {
        var report = await CreateService(StandardFeeds()).GetNewsAsync();

        var rail = report.Categories[0].Articles.Single(a => a.Title == "Light rail line opens");
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 14, 0, 0, TimeSpan.Zero), rail.Published.ToUniversalTime());
        Assert.Equal("About Light rail line opens", rail.Summary);
        Assert.Equal("Local Paper", rail.Source);

        var atom = report.Categories[1].Articles.Single(a => a.Source == "Military Blog");
        Assert.Equal("Army updates fitness test", atom.Title);
        Assert.StartsWith("https://blog.example.test/", atom.Url);
        Assert.Null(atom.Summary);
    }

    [Fact]
    public async Task FailingFeed_IsReportedForItsCategoryOnly()
    {
        var handler = new FakeHttpHandler()
            .Fail(TvUrl)
            .Xml(LocalUrl, Rss(("Light rail line opens", "Thu, 22 Oct 2026 09:00:00 -0500")))
            .Xml(MilitaryUrl, "<not really xml")
            .Xml(VaUrl, Rss(("VA expands clinic hours", "Wed, 21 Oct 2026 16:00:00 GMT")));

        var report = await CreateService(handler).GetNewsAsync();

        Assert.Equal(["Local TV"], report.Categories[0].FailedSources);
        Assert.Equal(["Military Blog"], report.Categories[1].FailedSources);
        Assert.Single(report.Categories[0].Articles);
        Assert.Single(report.Categories[1].Articles);
    }

    [Fact]
    public async Task EveryFeedFailing_Throws()
    {
        var handler = new FakeHttpHandler().Fail("example.test");

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(handler).GetNewsAsync());
    }

    [Fact]
    public async Task Report_IsCached()
    {
        var handler = StandardFeeds();
        var service = CreateService(handler);

        await service.GetNewsAsync();
        var requests = handler.Requests.Count;
        await service.GetNewsAsync();

        Assert.Equal(4, requests);
        Assert.Equal(requests, handler.Requests.Count);
    }
}
