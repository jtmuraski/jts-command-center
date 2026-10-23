using System.Xml;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using JtsCommandCenter.Core.News;
using static JtsCommandCenter.Infrastructure.News.FeedReader;

namespace JtsCommandCenter.Infrastructure.News;

/// <summary>
/// General news by category (Minnesota, military, politics, ...), each category merged from its configured
/// RSS/Atom feeds. Every feed is fetched in parallel and one failing feed only shows up in that category's
/// <see cref="NewsCategory.FailedSources"/>.
/// </summary>
public sealed class NewsService(
    HttpClient http,
    IMemoryCache cache,
    IOptions<NewsOptions> options,
    TimeProvider time,
    ILogger<NewsService> logger) : INewsService
{
    private const string CacheKey = "news";

    private readonly NewsOptions settings = options.Value;

    public async Task<NewsReport> GetNewsAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out NewsReport? cached) && cached is not null)
        {
            return cached;
        }

        var now = time.GetUtcNow();
        var oldest = now.AddDays(-settings.MaxAgeDays);
        var categories = await Task.WhenAll(settings.Categories.Select(c => LoadCategoryAsync(c, oldest, cancellationToken)));

        var feedCount = settings.Categories.Sum(c => c.Feeds.Count);
        var failedCount = categories.Sum(c => c.FailedSources.Count);
        if (feedCount > 0 && failedCount == feedCount)
        {
            throw new InvalidOperationException("No news feeds could be loaded.");
        }

        var report = new NewsReport(categories, now);

        // A partial result is retried sooner so a briefly-down feed comes back quickly.
        var ttl = failedCount > 0 ? TimeSpan.FromMinutes(2) : TimeSpan.FromMinutes(settings.CacheMinutes);
        cache.Set(CacheKey, report, ttl);
        return report;
    }

    private async Task<NewsCategory> LoadCategoryAsync(NewsCategoryOptions category, DateTimeOffset oldest, CancellationToken cancellationToken)
    {
        var failed = new List<string>();
        var feeds = await Task.WhenAll(category.Feeds.Select(f => FetchFeedAsync(f, failed, cancellationToken)));

        var exclude = settings.ExcludeKeywords.Concat(category.ExcludeKeywords).ToList();
        var articles = Merge(feeds.SelectMany(a => a).Where(a => a.Published >= oldest), exclude, settings.ArticlesPerCategory);

        // Keep failures in configured feed order, not completion order.
        var failedInOrder = category.Feeds.Select(f => f.Name).Where(failed.Contains).ToList();
        return new NewsCategory(category.Key, category.Name, articles, failedInOrder);
    }

    private async Task<List<NewsArticle>> FetchFeedAsync(NewsFeedOptions feed, List<string> failed, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadAsync(http, feed, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or XmlException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to load news feed {Feed}", feed.Name);
            lock (failed) failed.Add(feed.Name);
            return [];
        }
    }
}
