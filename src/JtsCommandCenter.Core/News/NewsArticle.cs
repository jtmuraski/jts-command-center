namespace JtsCommandCenter.Core.News;

/// <summary>One story from a news source (an RSS/Atom feed or ESPN's news API).</summary>
/// <param name="Summary">Plain-text teaser, when the source provides one.</param>
public sealed record NewsArticle(string Title, string Url, string Source, DateTimeOffset Published, string? Summary);

/// <summary>An RSS 2.0 or Atom feed.</summary>
public sealed class NewsFeedOptions
{
    /// <summary>Source name shown next to each headline, e.g. "Star Tribune".</summary>
    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;
}
