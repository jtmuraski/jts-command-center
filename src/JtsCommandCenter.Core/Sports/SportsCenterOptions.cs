namespace JtsCommandCenter.Core.Sports;

/// <summary>Settings bound from the "SportsCenter" section of appsettings.json (the Sports Center pages).</summary>
public sealed class SportsCenterOptions
{
    public const string SectionName = "SportsCenter";

    /// <summary>How long merged news is reused before the feeds are fetched again.</summary>
    public int NewsCacheMinutes { get; set; } = 15;

    /// <summary>How long a scoreboard week with upcoming games is reused.</summary>
    public int ScoreboardCacheMinutes { get; set; } = 5;

    /// <summary>How long a scoreboard week where every game is final is reused.</summary>
    public int CompletedWeekCacheMinutes { get; set; } = 60;

    /// <summary>How long a scoreboard week with a game in progress is reused.</summary>
    public int LiveCacheSeconds { get; set; } = 60;

    /// <summary>Per-league settings, keyed by ESPN league slug ("nfl", "mlb", "nhl").</summary>
    public Dictionary<string, LeagueOptions> Leagues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class LeagueOptions
{
    /// <summary>Display name, e.g. "NFL".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>ESPN sport slug: "football", "baseball", "hockey".</summary>
    public string Sport { get; set; } = string.Empty;

    /// <summary>ESPN league slug: "nfl", "mlb", "nhl".</summary>
    public string League { get; set; } = string.Empty;

    /// <summary>Team abbreviations whose games are pinned to the top of the scoreboard, e.g. "MIN".</summary>
    public List<string> PinnedTeams { get; set; } = [];

    /// <summary>Number of league headlines to show.</summary>
    public int HeadlineCount { get; set; } = 20;

    /// <summary>RSS/Atom feeds merged with ESPN's league news.</summary>
    public List<NewsFeedOptions> Feeds { get; set; } = [];

    /// <summary>Headlines containing any of these phrases are dropped (e.g. sportsbook promos).</summary>
    public List<string> ExcludeKeywords { get; set; } = [];

    /// <summary>A team that gets its own news section.</summary>
    public FocusTeamOptions? FocusTeam { get; set; }
}

public sealed class FocusTeamOptions
{
    /// <summary>Section heading, e.g. "Vikings".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>ESPN team id, used to pick team-tagged articles out of ESPN's league news.</summary>
    public string EspnTeamId { get; set; } = string.Empty;

    /// <summary>League-feed headlines containing any of these words are also counted as team news.</summary>
    public List<string> Keywords { get; set; } = [];

    /// <summary>Team-specific feeds (team site, team blogs).</summary>
    public List<NewsFeedOptions> Feeds { get; set; } = [];

    public int ArticleCount { get; set; } = 12;
}

public sealed class NewsFeedOptions
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}
