using System.Globalization;

namespace JtsCommandCenter.Core.Sports;

/// <summary>ESPN sport slugs, as used in settings (<see cref="LeagueOptions.Sport"/>) and on <see cref="GameDetail.Sport"/>.</summary>
public static class SportSlug
{
    public const string Football = "football";
    public const string Baseball = "baseball";
    public const string Hockey = "hockey";

    public static bool Is(string sport, string slug) => string.Equals(sport, slug, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Names for a game's periods: quarters (football), innings (baseball), or periods (hockey), including
/// overtime. Hockey's fifth period is a shootout in the regular season but a second overtime in the playoffs.
/// </summary>
public static class GamePeriods
{
    private const int Quarters = 4;
    private const int HockeyPeriods = 3;
    private const int Postseason = 3;

    /// <summary>Line score column header: "1".."4", "OT", "OT2" · "1".."9", "10" · "1".."3", "OT", "SO" (or "2OT").</summary>
    public static string Column(string sport, int seasonType, int period) =>
        sport.ToLowerInvariant() switch
        {
            SportSlug.Baseball => Number(period),
            SportSlug.Hockey => period <= HockeyPeriods ? Number(period) : HockeyExtra(seasonType, period),
            _ => period <= Quarters ? Number(period) : FootballOvertime(period)
        };

    /// <summary>Short name for play times and chart labels: "Q3", "OT" · "7th" · "2nd", "OT", "SO".</summary>
    public static string Short(string sport, int seasonType, int period) =>
        sport.ToLowerInvariant() switch
        {
            SportSlug.Baseball => Ordinal(Math.Max(period, 1)),
            SportSlug.Hockey => period <= HockeyPeriods ? Ordinal(Math.Max(period, 1)) : HockeyExtra(seasonType, period),
            _ => period <= Quarters ? $"Q{Math.Max(period, 1)}" : FootballOvertime(period)
        };

    /// <summary>Heading: "3rd quarter", "Overtime" · "7th inning" · "2nd period", "Shootout", "2nd overtime".</summary>
    public static string Name(string sport, int seasonType, int period) =>
        sport.ToLowerInvariant() switch
        {
            SportSlug.Baseball => $"{Ordinal(Math.Max(period, 1))} inning",
            SportSlug.Hockey => period <= HockeyPeriods ? $"{Ordinal(Math.Max(period, 1))} period"
                : period == HockeyPeriods + 1 ? "Overtime"
                : seasonType != Postseason ? "Shootout"
                : $"{Ordinal(period - HockeyPeriods)} overtime",
            _ => period <= Quarters ? $"{Ordinal(Math.Max(period, 1))} quarter"
                : period == Quarters + 1 ? "Overtime"
                : $"Overtime {period - Quarters}"
        };

    /// <summary>1st, 2nd, 3rd, 4th, ... 11th, 12th, 13th, ... 21st.</summary>
    public static string Ordinal(int number)
    {
        var suffix = (number % 100) is 11 or 12 or 13 ? "th"
            : (number % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return Number(number) + suffix;
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string FootballOvertime(int period) => period == Quarters + 1 ? "OT" : $"OT{period - Quarters}";

    // Regular season: one overtime, then a shootout. Playoffs: overtimes until someone scores.
    private static string HockeyExtra(int seasonType, int period) =>
        period == HockeyPeriods + 1 ? "OT"
        : seasonType != Postseason ? "SO"
        : $"{period - HockeyPeriods}OT";
}

/// <summary>Baseball at-bat outcomes, as ESPN names them on the pitch that ends the at-bat.</summary>
public static class BaseballPlays
{
    public const string PitchingChange = "Pitching Change";

    private static readonly HashSet<string> Hits = new(StringComparer.OrdinalIgnoreCase)
    {
        "Single", "Double", "Triple", "Home Run", "Bunt Single", "Infield Single", "Ground Rule Double", "Inside the Park Home Run"
    };

    public static bool IsHit(string outcome) => Hits.Contains(outcome);
}
