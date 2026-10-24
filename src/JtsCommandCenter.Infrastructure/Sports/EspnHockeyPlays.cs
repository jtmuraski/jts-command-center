using System.Text.Json;
using JtsCommandCenter.Core.Sports;
using static JtsCommandCenter.Infrastructure.Sports.EspnJson;

namespace JtsCommandCenter.Infrastructure.Sports;

/// <summary>
/// Hockey parts of ESPN's "summary" endpoint: a flat list of events (faceoffs, shots, hits, penalties,
/// goals), grouped into periods for the play-by-play.
/// </summary>
internal static class EspnHockeyPlays
{
    // Shots on goal: saved shots plus goals ("Missed" and "Blocked" attempts don't count).
    private static readonly HashSet<string> ShotsOnGoal = new(StringComparer.OrdinalIgnoreCase) { "Shot", "Goal" };

    public static (List<Drive> Periods, List<ScoringPlay> ScoringPlays) Parse(JsonElement root, GameTeam away, GameTeam home, int seasonType)
    {
        var teams = new Dictionary<string, GameTeam> { [away.Id] = away, [home.Id] = home };
        var periods = new List<Drive>();
        var scoring = new List<ScoringPlay>();

        foreach (var period in Array(root, "plays").GroupBy(Period))
        {
            var plays = new List<Play>();
            var shots = new Dictionary<string, int> { [away.Id] = 0, [home.Id] = 0 };

            foreach (var p in period)
            {
                var type = p.TryGetProperty("type", out var t) ? t : default;
                var typeText = type.ValueKind == JsonValueKind.Object ? Str(type, "text") ?? "" : "";
                var teamId = p.TryGetProperty("team", out var team) ? Str(team, "id") : null;
                var penaltyMinutes = type.ValueKind == JsonValueKind.Object ? Str(type, "penaltyMinutes") : null;
                var isGoal = Bool(p, "scoringPlay");
                var strength = p.TryGetProperty("strength", out var s) ? s : default;
                var strengthKey = strength.ValueKind == JsonValueKind.Object ? Str(strength, "abbreviation") : null;

                if (teamId is not null && shots.ContainsKey(teamId) && ShotsOnGoal.Contains(typeText))
                {
                    shots[teamId]++;
                }

                var text = Str(p, "text") ?? "";
                plays.Add(new Play(
                    Str(p, "id") ?? "",
                    period.Key,
                    Clock(p),
                    // Only an unusual strength is worth a tag: "Power Play", "Short Handed".
                    isGoal && strengthKey is not null and not "even-strength" ? Str(strength, "text") : null,
                    typeText,
                    penaltyMinutes is null ? text : $"{text} ({penaltyMinutes} min)",
                    null,
                    isGoal,
                    false,
                    penaltyMinutes is not null,
                    Int(p, "awayScore") ?? 0,
                    Int(p, "homeScore") ?? 0));

                if (isGoal && teamId is not null && teams.TryGetValue(teamId, out var scorer))
                {
                    scoring.Add(new ScoringPlay(
                        period.Key,
                        Clock(p),
                        scorer.Abbreviation,
                        scorer.LogoUrl,
                        strength.ValueKind == JsonValueKind.Object ? Str(strength, "text") ?? "Goal" : "Goal",
                        GoalBadge(strengthKey, period.Key, seasonType),
                        text,
                        Int(p, "awayScore") ?? 0,
                        Int(p, "homeScore") ?? 0));
                }
            }

            if (plays.Count == 0)
            {
                continue;
            }

            var goals = plays.Count(p => p.IsScoring);
            var isShootout = GamePeriods.Short(SportSlug.Hockey, seasonType, period.Key) == "SO";

            // ESPN keeps a shootout's own tally in the play scores (the game score doesn't change).
            var last = plays[^1];
            var summary = isShootout
                ? $"{away.Abbreviation} {last.AwayScore}, {home.Abbreviation} {last.HomeScore}"
                : $"{goals} goal{(goals == 1 ? "" : "s")} · shots {away.Abbreviation} {shots[away.Id]}, {home.Abbreviation} {shots[home.Id]}";

            periods.Add(new Drive(
                "",
                null,
                summary,
                GamePeriods.Name(SportSlug.Hockey, seasonType, period.Key),
                goals > 0,
                period.Key,
                "",
                null,
                null,
                0,
                plays));
        }

        return (periods, scoring);
    }

    private static string GoalBadge(string? strength, int period, int seasonType) =>
        GamePeriods.Short(SportSlug.Hockey, seasonType, period) == "SO" ? "SO"
        : strength switch
        {
            "power-play" => "PPG",
            "short-handed" => "SHG",
            _ => "G"
        };

    /// <summary>Probable starting goalies, e.g. "Filip Gustavsson" with "8-3-1, 2.41 GAA, .915 SV%".</summary>
    public static List<ProbableStarter> Starters(JsonElement root, string awayAbbreviation, string homeAbbreviation)
    {
        if (!root.TryGetProperty("goalies", out var goalies) || goalies.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return new[] { ("awayTeam", awayAbbreviation), ("homeTeam", homeAbbreviation) }
            .Select(side =>
            {
                var goalie = goalies.TryGetProperty(side.Item1, out var team) ? Array(team, "athletes").FirstOrDefault() : default;
                if (goalie.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                // ESPN lists some stats twice (e.g. "wins" as WINS and as W); the first is the clean one.
                var stats = Array(goalie, "statistics")
                    .GroupBy(st => Str(st, "name") ?? "")
                    .ToDictionary(g => g.Key, g => Str(g.First(), "displayValue"));
                var parts = new List<string>();
                if (stats.GetValueOrDefault("wins") is string w && stats.GetValueOrDefault("losses") is string l)
                {
                    parts.Add(stats.GetValueOrDefault("overtimeLosses") is string otl ? $"{w}-{l}-{otl}" : $"{w}-{l}");
                }
                if (stats.GetValueOrDefault("avgGoalsAgainst") is string gaa) parts.Add($"{gaa} GAA");
                if (stats.GetValueOrDefault("savePct") is string pct) parts.Add($"{pct} SV%");

                return new ProbableStarter(side.Item2, Str(goalie, "displayName") ?? "", parts.Count > 0 ? string.Join(", ", parts) : null);
            })
            .OfType<ProbableStarter>()
            .Where(s => s.Name.Length > 0)
            .ToList();
    }
}
