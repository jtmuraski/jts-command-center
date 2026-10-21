namespace JtsCommandCenter.Core.Sports;

/// <summary>One page of the league calendar, e.g. regular season week 7 or the Wild Card round.</summary>
/// <param name="SeasonType">ESPN season type: 1 preseason, 2 regular season, 3 postseason.</param>
public sealed record WeekRef(int SeasonType, int Week, string Label, DateOnly Start, DateOnly End);

/// <param name="Record">Overall record going into (or out of) the game, e.g. "4-2".</param>
public sealed record ScoreboardTeam(
    string Abbreviation,
    string Name,
    string ShortName,
    string? LogoUrl,
    int? Score,
    string? Record,
    bool IsWinner);

public sealed record ByeTeam(string Abbreviation, string Name, string? LogoUrl);

/// <param name="Start">Kickoff, in the configured time zone.</param>
/// <param name="Location">"City, ST" of the venue.</param>
/// <param name="Note">Special billing from ESPN, e.g. "NFL London Games" or a playoff round.</param>
public sealed record ScoreboardGame(
    string Id,
    DateTime Start,
    bool TimeTbd,
    GameState State,
    string StatusDetail,
    ScoreboardTeam Away,
    ScoreboardTeam Home,
    string? Venue,
    string? Location,
    string? Broadcast,
    string? Note)
{
    public bool Involves(IEnumerable<string> abbreviations) =>
        abbreviations.Any(a => string.Equals(a, Away.Abbreviation, StringComparison.OrdinalIgnoreCase)
            || string.Equals(a, Home.Abbreviation, StringComparison.OrdinalIgnoreCase));
}

/// <param name="Weeks">Every regular-season and postseason week, in order, for paging.</param>
/// <param name="TeamsOnBye">Teams with no game this week.</param>
/// <param name="UpdatedAt">When the scoreboard was fetched, in the configured time zone.</param>
public sealed record WeekScoreboard(
    int Season,
    WeekRef Week,
    IReadOnlyList<WeekRef> Weeks,
    IReadOnlyList<ScoreboardGame> Games,
    IReadOnlyList<ByeTeam> TeamsOnBye,
    DateTime UpdatedAt)
{
    public bool HasLiveGame => Games.Any(g => g.State == GameState.Live);
    public bool IsComplete => Games.Count > 0 && Games.All(g => g.State is GameState.Final or GameState.Postponed);

    public int WeekIndex => Weeks.ToList().FindIndex(w => w.SeasonType == Week.SeasonType && w.Week == Week.Week);
    public WeekRef? Previous => WeekIndex > 0 ? Weeks[WeekIndex - 1] : null;
    public WeekRef? Next => WeekIndex >= 0 && WeekIndex < Weeks.Count - 1 ? Weeks[WeekIndex + 1] : null;
}

public interface IScoreboardService
{
    /// <summary>
    /// Games for one week of the league calendar. With no week given, returns ESPN's current week.
    /// </summary>
    Task<WeekScoreboard> GetWeekAsync(string league, WeekRef? week = null, CancellationToken cancellationToken = default);
}
