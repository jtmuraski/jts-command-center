namespace JtsCommandCenter.Core.Sports;

/// <summary>Settings bound from the "Sports" section of appsettings.json.</summary>
public sealed class SportsOptions
{
    public const string SectionName = "Sports";

    /// <summary>IANA or Windows time zone id that game times are shown in.</summary>
    public string TimeZone { get; set; } = "America/Chicago";

    /// <summary>How long a fetched report is reused when no game is in progress.</summary>
    public int CacheMinutes { get; set; } = 10;

    /// <summary>How long a fetched report is reused while a game is live (or a team failed to load).</summary>
    public int LiveCacheSeconds { get; set; } = 60;

    /// <summary>How often an open dashboard re-requests scores when nothing is live.</summary>
    public int RefreshMinutes { get; set; } = 10;

    /// <summary>Teams shown on the card, in display order.</summary>
    public List<TeamOptions> Teams { get; set; } = [];
}

public sealed class TeamOptions
{
    /// <summary>Section heading, e.g. "Football".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>ESPN sport slug: "football", "baseball", "hockey", ...</summary>
    public string Sport { get; set; } = string.Empty;

    /// <summary>ESPN league slug: "nfl", "mlb", "nhl", ...</summary>
    public string League { get; set; } = string.Empty;

    /// <summary>ESPN team abbreviation or id, e.g. "min".</summary>
    public string Team { get; set; } = string.Empty;

    public SportsDisplay Display { get; set; } = SportsDisplay.LastAndNext;

    /// <summary>Always show records as W-L-T, even when the team has no ties (ESPN omits a zero).</summary>
    public bool AlwaysShowTies { get; set; }
}
