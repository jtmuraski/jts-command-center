namespace JtsCommandCenter.Core.Sports;

/// <summary>Everything about one game: box score, scoring, play-by-play, and pregame context.</summary>
/// <param name="Start">Kickoff, in the configured time zone.</param>
/// <param name="StatusDetail">ESPN's short status, e.g. "Final/OT", "Q3 4:12", "Halftime".</param>
/// <param name="SeasonType">ESPN season type of the game (2 regular season, 3 postseason).</param>
/// <param name="Week">Week number within <paramref name="SeasonType"/>, used to link back to the scoreboard.</param>
/// <param name="PeriodLabels">Column headers for the line score: "1".."4", then "OT", "OT2", ...</param>
/// <param name="TeamStats">Side-by-side team totals; empty before kickoff.</param>
/// <param name="PlayerStats">Box score tables, one per team per category; empty before kickoff.</param>
/// <param name="Drives">Every drive in order, each with its plays; empty before kickoff.</param>
/// <param name="WinProbability">Home team's win chance after each play; empty before kickoff.</param>
/// <param name="Leaders">Game leaders once play starts, season leaders before.</param>
/// <param name="Pregame">Predictor, betting line, and recent form; null once the game has started.</param>
public sealed record GameDetail(
    string Id,
    GameState State,
    string StatusDetail,
    DateTime Start,
    bool TimeTbd,
    int SeasonType,
    int? Week,
    GameTeam Away,
    GameTeam Home,
    IReadOnlyList<string> PeriodLabels,
    GameInfo Info,
    IReadOnlyList<TeamStatLine> TeamStats,
    IReadOnlyList<PlayerStatTable> PlayerStats,
    IReadOnlyList<ScoringPlay> ScoringPlays,
    IReadOnlyList<Drive> Drives,
    IReadOnlyList<WinProbabilityPoint> WinProbability,
    IReadOnlyList<TeamLeaders> Leaders,
    IReadOnlyList<TeamInjuries> Injuries,
    Pregame? Pregame,
    DateTime UpdatedAt)
{
    public GameTeam Team(string abbreviation) =>
        string.Equals(Away.Abbreviation, abbreviation, StringComparison.OrdinalIgnoreCase) ? Away : Home;

    public int PlayCount => Drives.Sum(d => d.Plays.Count);
}

/// <param name="Linescores">Points per period, aligned with <see cref="GameDetail.PeriodLabels"/>.</param>
/// <param name="HasPossession">This team has the ball right now (live games only).</param>
public sealed record GameTeam(
    string Id,
    string Abbreviation,
    string Name,
    string ShortName,
    string? LogoUrl,
    string? Record,
    int? Score,
    IReadOnlyList<int> Linescores,
    bool IsWinner,
    bool HasPossession);

/// <param name="Weather">Short forecast for outdoor games, e.g. "52° · 51% precip · gusts 12 mph".</param>
public sealed record GameInfo(
    string? Venue,
    string? Location,
    int? Attendance,
    string? Broadcast,
    string? Weather,
    IReadOnlyList<Official> Officials);

public sealed record Official(string Name, string Position);

/// <param name="AwayNumber">Numeric value for comparison bars, when the stat is a plain number.</param>
/// <param name="LowerIsBetter">True for stats like turnovers and penalties.</param>
public sealed record TeamStatLine(
    string Key,
    string Label,
    string AwayValue,
    string HomeValue,
    double? AwayNumber,
    double? HomeNumber,
    bool LowerIsBetter);

/// <param name="Category">ESPN category key, e.g. "passing", "rushing", "defensive".</param>
/// <param name="Title">Display title, e.g. "Passing".</param>
/// <param name="Labels">Column headers, e.g. C/ATT, YDS, AVG, TD, INT.</param>
/// <param name="Totals">Team totals row, when ESPN provides one.</param>
public sealed record PlayerStatTable(
    string TeamAbbreviation,
    string Category,
    string Title,
    IReadOnlyList<string> Labels,
    IReadOnlyList<PlayerStatRow> Players,
    IReadOnlyList<string>? Totals);

public sealed record PlayerStatRow(string Name, string? Jersey, IReadOnlyList<string> Stats);

/// <param name="Type">Long form, e.g. "Passing Touchdown", "Field Goal Good".</param>
/// <param name="TypeAbbreviation">Short form, e.g. "TD", "FG".</param>
public sealed record ScoringPlay(
    int Period,
    string Clock,
    string TeamAbbreviation,
    string? TeamLogoUrl,
    string Type,
    string TypeAbbreviation,
    string Text,
    int AwayScore,
    int HomeScore);

/// <param name="Summary">ESPN's one-liner, e.g. "9 plays, 51 yards, 5:56".</param>
/// <param name="Result">How it ended, e.g. "Touchdown", "Punt", "Interception".</param>
/// <param name="StartPosition">Field position at the start, e.g. "MIN 28".</param>
public sealed record Drive(
    string TeamAbbreviation,
    string? TeamLogoUrl,
    string Summary,
    string Result,
    bool IsScore,
    int StartPeriod,
    string StartClock,
    string? StartPosition,
    string? EndPosition,
    int Yards,
    IReadOnlyList<Play> Plays);

/// <param name="DownDistance">Situation before the snap, e.g. "3rd & 9 at NO 28".</param>
/// <param name="Type">ESPN play type, e.g. "Rush", "Pass Reception", "Punt".</param>
/// <param name="Yards">Yards gained on the play, when it's a from-scrimmage play.</param>
public sealed record Play(
    string Id,
    int Period,
    string Clock,
    string? DownDistance,
    string Type,
    string Text,
    int? Yards,
    bool IsScoring,
    bool IsTurnover,
    bool IsPenalty,
    int AwayScore,
    int HomeScore);

/// <param name="HomeWinPercentage">0–1 chance the home team wins, after this play.</param>
public sealed record WinProbabilityPoint(int Period, double HomeWinPercentage);

public sealed record TeamLeaders(string TeamAbbreviation, IReadOnlyList<Leader> Leaders);

/// <param name="Category">Display name, e.g. "Passing Yards".</param>
/// <param name="Value">ESPN's stat line, e.g. "24/30, 229 YDS, 1 TD".</param>
public sealed record Leader(string Category, string Name, string? Position, string Value);

public sealed record TeamInjuries(string TeamAbbreviation, IReadOnlyList<Injury> Injuries);

/// <param name="Status">Game status, e.g. "Questionable", "Out".</param>
/// <param name="Detail">Body part or reason, e.g. "Hamstring".</param>
public sealed record Injury(string Name, string? Position, string Status, string? Detail);

/// <param name="HomeWinChance">ESPN's matchup predictor, 0–100.</param>
public sealed record Pregame(
    double? HomeWinChance,
    double? AwayWinChance,
    BettingLine? Line,
    IReadOnlyList<TeamForm> RecentForm);

/// <param name="Details">Spread as ESPN states it, e.g. "MIN -2.5".</param>
public sealed record BettingLine(
    string Provider,
    string? Details,
    double? OverUnder,
    int? AwayMoneyLine,
    int? HomeMoneyLine);

/// <summary>A team's last few results going into this game.</summary>
public sealed record TeamForm(string TeamAbbreviation, IReadOnlyList<RecentGame> Games);

/// <param name="Result">"W", "L", or "T".</param>
/// <param name="Score">Final score as ESPN shows it, e.g. "33-30 OT".</param>
/// <param name="AtVs">"vs" for home games, "@" for road games.</param>
public sealed record RecentGame(string Result, string Score, string AtVs, string Opponent);

public interface IGameDetailService
{
    /// <summary>Full detail for one game, by ESPN event id.</summary>
    Task<GameDetail> GetGameAsync(string league, string eventId, CancellationToken cancellationToken = default);
}
