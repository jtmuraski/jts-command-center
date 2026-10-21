namespace JtsCommandCenter.Web.Services.Sports;

/// <summary>How a team's section is laid out on the card.</summary>
public enum SportsDisplay
{
    /// <summary>The last completed game and the next (or live) game.</summary>
    LastAndNext,

    /// <summary>Every game of the current series, plus the next series (baseball).</summary>
    Series
}

public enum GameState
{
    Scheduled,
    Live,
    Final,
    Postponed
}

public enum GameResult
{
    Win,
    Loss,
    Tie
}

/// <param name="UpdatedAt">When the report was fetched, in the configured time zone.</param>
public sealed record SportsReport(IReadOnlyList<TeamReport> Teams, DateTime UpdatedAt)
{
    public bool HasLiveGame => Teams.Any(t => t.HasLiveGame);
}

public sealed record TeamReport
{
    public required string Label { get; init; }
    public required SportsDisplay Display { get; init; }
    public required string TeamName { get; init; }
    public required string ShortName { get; init; }

    /// <summary>Current record, e.g. "5-0" or "6-2-2".</summary>
    public string? Record { get; init; }

    /// <summary>Standing summary from ESPN, e.g. "1st in NFC North".</summary>
    public string? Standing { get; init; }

    /// <summary>Set once the team's season (including any postseason) is over.</summary>
    public SeasonRecap? Recap { get; init; }

    public Game? LastGame { get; init; }

    /// <summary>The game in progress, or else the next scheduled game.</summary>
    public Game? NextGame { get; init; }

    public Series? CurrentSeries { get; init; }
    public Series? NextSeries { get; init; }

    /// <summary>True when this team's schedule couldn't be loaded.</summary>
    public bool IsUnavailable { get; init; }

    public bool HasLiveGame =>
        NextGame?.State == GameState.Live
        || CurrentSeries?.Games.Any(g => g.State == GameState.Live) == true;
}

/// <param name="Season">Season label from ESPN, e.g. "2026" or "2026-27".</param>
/// <param name="Place">Division finish, e.g. "3rd", when known.</param>
/// <param name="Division">Full division name, e.g. "American League Central", when known.</param>
public sealed record SeasonRecap(string Season, string Record, string? Place, string? Division);

public sealed record Opponent(string Abbreviation, string Name, string? LogoUrl);

/// <param name="Start">Scheduled start, in the configured time zone.</param>
/// <param name="TimeTbd">The date is set but the start time isn't.</param>
/// <param name="StatusDetail">ESPN's short status, e.g. "Final/OT", "Q3 4:12", "Bot 7th".</param>
/// <param name="Label">Extra context such as "Week 7", when ESPN provides it.</param>
public sealed record Game(
    string Id,
    DateTime Start,
    bool TimeTbd,
    GameState State,
    string StatusDetail,
    bool IsHome,
    Opponent Opponent,
    int? Score,
    int? OpponentScore,
    bool IsPostseason,
    string? Label)
{
    public GameResult? Result => State == GameState.Final && Score is int us && OpponentScore is int them
        ? us > them ? GameResult.Win : us < them ? GameResult.Loss : GameResult.Tie
        : null;
}

/// <summary>Consecutive games against the same opponent at the same park.</summary>
public sealed record Series(Opponent Opponent, bool IsHome, bool IsPostseason, IReadOnlyList<Game> Games)
{
    public DateOnly FirstDay => DateOnly.FromDateTime(Games[0].Start);
    public DateOnly LastDay => DateOnly.FromDateTime(Games[^1].Start);
    public int Wins => Games.Count(g => g.Result == GameResult.Win);
    public int Losses => Games.Count(g => g.Result == GameResult.Loss);
    public bool IsComplete => Games.All(g => g.State is GameState.Final or GameState.Postponed);
}
