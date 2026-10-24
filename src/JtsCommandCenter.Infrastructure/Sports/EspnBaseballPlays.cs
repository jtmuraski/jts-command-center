using System.Text.Json;
using JtsCommandCenter.Core.Sports;
using static JtsCommandCenter.Infrastructure.Sports.EspnJson;

namespace JtsCommandCenter.Infrastructure.Sports;

/// <summary>
/// Baseball parts of ESPN's "summary" endpoint. Its play list is pitch by pitch; the play-by-play keeps
/// each at-bat's result (and pitching changes, steals, ...), grouped into half-innings.
/// </summary>
internal static class EspnBaseballPlays
{
    // Badges for scoring plays, keyed by at-bat outcome.
    private static readonly Dictionary<string, string> OutcomeBadges = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Single"] = "1B",
        ["Bunt Single"] = "1B",
        ["Infield Single"] = "1B",
        ["Double"] = "2B",
        ["Ground Rule Double"] = "2B",
        ["Triple"] = "3B",
        ["Home Run"] = "HR",
        ["Inside the Park Home Run"] = "HR",
        ["Walk"] = "BB",
        ["Hit By Pitch"] = "HBP",
        ["Sacrifice Fly"] = "SF",
    };

    // Team stats worth comparing: game totals once play starts, season figures before.
    private static readonly StatSpec[] GameStats =
    [
        new("batting", "hits", "Hits"),
        new("batting", "homeRuns", "Home runs"),
        new("batting", "RBIs", "RBIs"),
        new("batting", "walks", "Walks"),
        new("batting", "strikeouts", "Struck out", LowerIsBetter: true),
        new("batting", "stolenBases", "Stolen bases"),
        // Not "runnersLeftOnBase": it sums every batter's LOB, so it's about twice the official team figure.
        new("fielding", "errors", "Errors", LowerIsBetter: true),
        new("pitching", "pitches", "Pitches thrown", LowerIsBetter: true),
    ];

    private static readonly StatSpec[] SeasonStats =
    [
        new("batting", "avg", "Batting average"),
        new("batting", "OPS", "OPS"),
        new("batting", "runs", "Runs"),
        new("batting", "homeRuns", "Home runs"),
        new("batting", "stolenBases", "Stolen bases"),
        new("pitching", "ERA", "ERA", LowerIsBetter: true),
        new("pitching", "WHIP", "WHIP", LowerIsBetter: true),
        new("pitching", "strikeouts", "Pitching strikeouts"),
        new("pitching", "saves", "Saves"),
        new("fielding", "errors", "Errors", LowerIsBetter: true),
    ];

    private sealed record StatSpec(string Group, string Name, string Label, bool LowerIsBetter = false);

    public static (List<Drive> HalfInnings, List<ScoringPlay> ScoringPlays) Parse(JsonElement root, GameTeam away, GameTeam home)
    {
        var plays = Array(root, "plays").ToList();
        var halves = new List<Drive>();
        var scoring = new List<ScoringPlay>();

        foreach (var half in plays.Select((p, i) => (Play: p, Index: i)).GroupBy(p => (Inning: Period(p.Play), Side: HalfName(p.Play))))
        {
            var (inning, side) = half.Key;
            var batting = side == "Top" ? away : home;
            var results = new List<Play>();

            foreach (var (p, index) in half)
            {
                if (p.TryGetProperty("type", out var type) && Str(type, "type") == "play-result")
                {
                    results.Add(ResultPlay(p, inning, Outcome(plays, index)));
                }
            }

            if (results.Count == 0)
            {
                continue;
            }

            foreach (var play in results.Where(r => r.IsScoring))
            {
                scoring.Add(new ScoringPlay(
                    inning,
                    side,
                    batting.Abbreviation,
                    batting.LogoUrl,
                    play.Type,
                    OutcomeBadges.GetValueOrDefault(play.Type) ?? "R",
                    play.Text,
                    play.AwayScore,
                    play.HomeScore));
            }

            // Runs in the half are the batting team's score change across it.
            var before = half.First().Play;
            var runs = side == "Top"
                ? results[^1].AwayScore - (Int(before, "awayScore") ?? 0)
                : results[^1].HomeScore - (Int(before, "homeScore") ?? 0);
            var hits = results.Count(r => BaseballPlays.IsHit(r.Type));

            halves.Add(new Drive(
                batting.Abbreviation,
                batting.LogoUrl,
                $"{Count(runs, "run")} · {Count(hits, "hit")}",
                $"{side} {GamePeriods.Ordinal(inning)}",
                runs > 0,
                inning,
                side,
                null,
                null,
                0,
                results));
        }

        return (halves, scoring);
    }

    private static Play ResultPlay(JsonElement p, int inning, string outcome) => new(
        Str(p, "id") ?? "",
        inning,
        "",
        null,
        outcome,
        Str(p, "text") ?? "",
        null,
        Bool(p, "scoringPlay"),
        false,
        false,
        Int(p, "awayScore") ?? 0,
        Int(p, "homeScore") ?? 0,
        // ESPN resets the outs to 0 on a pitching change, whatever the inning's count.
        outcome == BaseballPlays.PitchingChange ? null : Int(p, "outs"));

    /// <summary>
    /// What a play result was: a pitching change, or else the event just before it — the last pitch of the
    /// at-bat ("Single", "Walk", "Strikeout") or a running play such as "Stolen Base".
    /// </summary>
    private static string Outcome(List<JsonElement> plays, int index)
    {
        var result = plays[index];
        if (Str(result, "summaryType") == "C")
        {
            return BaseballPlays.PitchingChange;
        }

        if (index == 0 || !plays[index - 1].TryGetProperty("type", out var previous))
        {
            return "";
        }

        // Pitches carry the at-bat outcome they'd produce as alternative text ("Single", or "Walk" for any
        // ball); a ball or strike only ends the at-bat on ball four or strike three.
        var pitch = plays[index - 1];
        var text = Str(previous, "text") ?? "";
        if (Str(pitch, "summaryType") != "P" || Str(previous, "alternativeText") is not string alternative)
        {
            return text;
        }

        var count = pitch.TryGetProperty("resultCount", out var c) ? c : default;
        return alternative switch
        {
            "Walk" when count.ValueKind != JsonValueKind.Object || Int(count, "balls") < 4 => text,
            "Strikeout" when count.ValueKind != JsonValueKind.Object || Int(count, "strikes") < 3 => text,
            _ => alternative
        };
    }

    private static string HalfName(JsonElement play) =>
        play.TryGetProperty("period", out var period) && Str(period, "type") is string type
            ? type.StartsWith("Bot", StringComparison.OrdinalIgnoreCase) ? "Bottom" : "Top"
            : "Top";

    private static string Count(int value, string noun) => $"{value} {noun}{(value == 1 ? "" : "s")}";

    /// <summary>Team stats from ESPN's grouped box score (batting, pitching, fielding).</summary>
    public static List<TeamStatLine> TeamStats(JsonElement root, string awayAbbreviation, bool started)
    {
        if (!root.TryGetProperty("boxscore", out var box))
        {
            return [];
        }

        var teams = Array(box, "teams").ToList();
        var awayJson = teams.FirstOrDefault(t => Str(t, "homeAway") == "away" || TeamAbbreviation(t) == awayAbbreviation);
        var homeJson = teams.FirstOrDefault(t => !t.Equals(awayJson));
        if (awayJson.ValueKind != JsonValueKind.Object || homeJson.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var lines = new List<TeamStatLine>();
        foreach (var spec in started ? GameStats : SeasonStats)
        {
            if (Stat(awayJson, spec) is not string awayValue || Stat(homeJson, spec) is not string homeValue)
            {
                continue;
            }

            lines.Add(new TeamStatLine(
                $"{spec.Group}.{spec.Name}",
                spec.Label,
                awayValue,
                homeValue,
                EspnGameDetailService.StatNumber(spec.Name, awayValue),
                EspnGameDetailService.StatNumber(spec.Name, homeValue),
                spec.LowerIsBetter));
        }

        return lines;
    }

    private static string? Stat(JsonElement team, StatSpec spec) =>
        Array(team, "statistics")
            .Where(g => Str(g, "name") == spec.Group)
            .SelectMany(g => Array(g, "stats"))
            .Where(s => Str(s, "name") == spec.Name)
            .Select(s => Str(s, "displayValue"))
            .FirstOrDefault();

    /// <summary>Probable starting pitchers from the header, e.g. "N. Martinez" with "1-0, 3.00 ERA".</summary>
    public static List<ProbableStarter> Starters(JsonElement awayJson, JsonElement homeJson) =>
        new[] { awayJson, homeJson }
            .Select(team =>
            {
                var probable = Array(team, "probables").FirstOrDefault();
                if (probable.ValueKind != JsonValueKind.Object || !probable.TryGetProperty("athlete", out var athlete))
                {
                    return null;
                }

                var stats = probable.TryGetProperty("statistics", out var statistics) && statistics.TryGetProperty("splits", out var splits)
                    ? Array(splits, "categories").GroupBy(c => Str(c, "name") ?? "").ToDictionary(g => g.Key, g => Str(g.First(), "displayValue"))
                    : [];
                var summary = stats.GetValueOrDefault("wins") is string w && stats.GetValueOrDefault("losses") is string l
                    ? $"{w}-{l}" + (stats.GetValueOrDefault("ERA") is string era ? $", {era} ERA" : "")
                    : Str(probable, "record")?.Trim('(', ')');

                return new ProbableStarter(TeamAbbreviation(team), Str(athlete, "displayName") ?? "", summary);
            })
            .OfType<ProbableStarter>()
            .Where(s => s.Name.Length > 0)
            .ToList();
}
