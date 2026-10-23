namespace JtsCommandCenter.Core.News;

/// <param name="Articles">Newest first, with duplicate headlines (same story from two outlets) removed.</param>
/// <param name="FailedSources">Feeds that couldn't be fetched this time (the rest still show).</param>
public sealed record NewsCategory(
    string Key,
    string Name,
    IReadOnlyList<NewsArticle> Articles,
    IReadOnlyList<string> FailedSources);

/// <param name="Categories">In configured order.</param>
public sealed record NewsReport(IReadOnlyList<NewsCategory> Categories, DateTimeOffset FetchedAt);

/// <summary>A dashboard headline, labeled with the category it came from.</summary>
public sealed record NewsHighlight(string CategoryKey, string CategoryName, NewsArticle Article);

public interface INewsService
{
    /// <summary>Every configured category, each merged from its feeds.</summary>
    Task<NewsReport> GetNewsAsync(CancellationToken cancellationToken = default);
}
