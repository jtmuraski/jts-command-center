using JtsCommandCenter.Core.News;

namespace JtsCommandCenter.Tests.News;

public class NewsMathTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 15, 0, 0, TimeSpan.Zero);

    private static NewsArticle Article(string title, string? url = null) =>
        new(title, url ?? $"https://example.test/{title}", "Source", Now, null);

    private static NewsCategory Category(string key, params NewsArticle[] articles) =>
        new(key, key.ToUpperInvariant(), articles, []);

    [Fact]
    public void Highlights_TakeEachCategoryNewestInCategoryOrder()
    {
        var report = new NewsReport(
            [
                Category("mn", Article("mn1"), Article("mn2")),
                Category("world", Article("w1"), Article("w2")),
            ],
            Now);

        var picks = NewsMath.Highlights(report, perCategory: 1);

        Assert.Equal(["mn1", "w1"], picks.Select(p => p.Article.Title));
        Assert.Equal("MN", picks[0].CategoryName);
        Assert.Equal("world", picks[1].CategoryKey);
    }

    [Fact]
    public void Highlights_RoundRobinAcrossCategories()
    {
        var report = new NewsReport(
            [
                Category("mn", Article("mn1"), Article("mn2"), Article("mn3")),
                Category("world", Article("w1")),
            ],
            Now);

        var picks = NewsMath.Highlights(report, perCategory: 2);

        Assert.Equal(["mn1", "w1", "mn2"], picks.Select(p => p.Article.Title));
    }

    [Fact]
    public void Highlights_SkipAStoryAlreadyPickedUnderAnotherCategory()
    {
        var shared = Article("Shared story", "https://example.test/shared");
        var report = new NewsReport(
            [
                Category("politics", shared),
                Category("military", shared, Article("m2")),
                Category("empty"),
            ],
            Now);

        var picks = NewsMath.Highlights(report, perCategory: 1);

        Assert.Equal(["Shared story", "m2"], picks.Select(p => p.Article.Title));
        Assert.Equal("politics", picks[0].CategoryKey);
    }
}
