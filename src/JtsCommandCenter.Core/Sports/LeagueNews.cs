using JtsCommandCenter.Core.News;

namespace JtsCommandCenter.Core.Sports;

/// <param name="TeamName">Focus team's name, or null when the league has no focus team configured.</param>
/// <param name="FailedSources">Sources that couldn't be fetched this time (the rest still show).</param>
public sealed record LeagueNews(
    string LeagueName,
    IReadOnlyList<NewsArticle> Headlines,
    string? TeamName,
    IReadOnlyList<NewsArticle> TeamNews,
    IReadOnlyList<string> FailedSources,
    DateTimeOffset FetchedAt);

public interface ISportsNewsService
{
    /// <summary>League headlines plus focus-team news, merged from ESPN and the configured feeds.</summary>
    Task<LeagueNews> GetNewsAsync(string league, CancellationToken cancellationToken = default);
}
