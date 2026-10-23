namespace JtsCommandCenter.Core.News;

/// <summary>Settings bound from the "News" section of appsettings.json (the dashboard card and News page).</summary>
public sealed class NewsOptions
{
    public const string SectionName = "News";

    /// <summary>How long merged news is reused before the feeds are fetched again.</summary>
    public int CacheMinutes { get; set; } = 15;

    /// <summary>How often an open dashboard or News page re-requests the news.</summary>
    public int RefreshMinutes { get; set; } = 15;

    /// <summary>Stories kept per category, newest first.</summary>
    public int ArticlesPerCategory { get; set; } = 30;

    /// <summary>Stories older than this are dropped (feeds sometimes carry months-old items).</summary>
    public int MaxAgeDays { get; set; } = 7;

    /// <summary>Headlines containing any of these phrases are dropped from every category (e.g. coupon posts).</summary>
    public List<string> ExcludeKeywords { get; set; } = [];

    /// <summary>Categories in display order.</summary>
    public List<NewsCategoryOptions> Categories { get; set; } = [];
}

public sealed class NewsCategoryOptions
{
    /// <summary>Stable id used in the News page's query string, e.g. "minnesota".</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Display name, e.g. "Minnesota".</summary>
    public string Name { get; set; } = string.Empty;

    public List<NewsFeedOptions> Feeds { get; set; } = [];

    /// <summary>Headlines containing any of these phrases are dropped from this category only.</summary>
    public List<string> ExcludeKeywords { get; set; } = [];
}
