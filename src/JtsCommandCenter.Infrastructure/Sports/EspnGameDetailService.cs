using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using JtsCommandCenter.Core.Sports;
using static JtsCommandCenter.Infrastructure.Sports.EspnJson;

namespace JtsCommandCenter.Infrastructure.Sports;

/// <summary>Game detail (box score, play-by-play, pregame) from ESPN's site API "summary" endpoint.</summary>
public sealed class EspnGameDetailService(
    HttpClient http,
    IMemoryCache cache,
    IOptions<SportsCenterOptions> options,
    IOptions<SportsOptions> sportsOptions,
    ILogger<EspnGameDetailService> logger) : IGameDetailService
{
    private const int RegulationPeriods = 4;

    // Box score categories in the order a football fan reads them; anything unknown goes after these.
    private static readonly string[] CategoryOrder =
        ["passing", "rushing", "receiving", "defensive", "interceptions", "fumbles", "kickReturns", "puntReturns", "kicking", "punting"];

    private static readonly Dictionary<string, string> CategoryTitles = new()
    {
        ["passing"] = "Passing",
        ["rushing"] = "Rushing",
        ["receiving"] = "Receiving",
        ["defensive"] = "Defense",
        ["interceptions"] = "Interceptions",
        ["fumbles"] = "Fumbles",
        ["kickReturns"] = "Kick Returns",
        ["puntReturns"] = "Punt Returns",
        ["kicking"] = "Kicking",
        ["punting"] = "Punting",
    };

    // Team stats where the smaller number is the better one (drives the comparison bars).
    private static readonly HashSet<string> LowerIsBetter =
        ["turnovers", "fumblesLost", "interceptions", "totalPenaltiesYards", "sacksYardsLost"];

    // Play types that carry a down-and-distance but aren't plays from scrimmage.
    private static readonly string[] NonSnapTypes = ["Timeout", "End Period", "End of Half", "End of Game", "Two-minute warning"];

    // "count-yards" stats (e.g. penalties "4-30") compare by count, not as a ratio.
    private static readonly HashSet<string> CountThenYards = ["totalPenaltiesYards", "sacksYardsLost"];

    private readonly SportsCenterOptions settings = options.Value;
    private readonly TimeZoneInfo zone = ResolveZone(sportsOptions.Value.TimeZone);

    public async Task<GameDetail> GetGameAsync(string league, string eventId, CancellationToken cancellationToken = default)
    {
        if (!settings.Leagues.TryGetValue(league, out var leagueOptions))
        {
            throw new InvalidOperationException($"No SportsCenter settings for league '{league}'.");
        }

        var cacheKey = $"game:{league}:{eventId}";
        if (cache.TryGetValue(cacheKey, out GameDetail? cached) && cached is not null)
        {
            return cached;
        }

        logger.LogInformation("Fetching {League} game {EventId} from ESPN", league, eventId);

        var url = $"apis/site/v2/sports/{leagueOptions.Sport}/{leagueOptions.League}/summary?event={Uri.EscapeDataString(eventId)}";
        using var doc = await http.GetJsonAsync(url, cancellationToken);
        var game = Parse(doc.RootElement, eventId);

        var ttl = game.State switch
        {
            GameState.Live => TimeSpan.FromSeconds(settings.LiveCacheSeconds),
            GameState.Final => TimeSpan.FromMinutes(settings.CompletedWeekCacheMinutes),
            _ => TimeSpan.FromMinutes(settings.ScoreboardCacheMinutes)
        };
        cache.Set(cacheKey, game, ttl);
        return game;
    }

    private GameDetail Parse(JsonElement root, string eventId)
    {
        var header = root.GetProperty("header");
        var competition = Array(header, "competitions").First();
        var status = competition.GetProperty("status").GetProperty("type");
        var state = ParseState(status);

        var competitors = Array(competition, "competitors").ToList();
        var awayJson = competitors.First(c => Str(c, "homeAway") == "away");
        var homeJson = competitors.First(c => Str(c, "homeAway") == "home");
        var away = ParseTeam(awayJson);
        var home = ParseTeam(homeJson);

        var start = DateTimeOffset.Parse(Str(competition, "date")!, CultureInfo.InvariantCulture);
        var timeValid = !competition.TryGetProperty("timeValid", out var tv) || tv.ValueKind != JsonValueKind.False;
        var seasonType = header.TryGetProperty("season", out var season) ? Int(season, "type") ?? 2 : 2;

        var drives = ParseDrives(root);
        var started = state is GameState.Live or GameState.Final;

        return new GameDetail(
            Str(header, "id") ?? eventId,
            state,
            Str(status, "shortDetail") ?? Str(status, "description") ?? "",
            TimeZoneInfo.ConvertTime(start, zone).DateTime,
            !timeValid,
            seasonType,
            Int(header, "week"),
            away,
            home,
            PeriodLabels(Math.Max(away.Linescores.Count, home.Linescores.Count)),
            ParseInfo(root, competition),
            ParseTeamStats(root, away.Abbreviation),
            ParsePlayerStats(root),
            ParseScoringPlays(root),
            drives,
            ParseWinProbability(root, drives),
            ParseLeaders(root),
            ParseInjuries(root),
            started ? null : ParsePregame(root, away.Id, home.Id),
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
    }

    private static GameTeam ParseTeam(JsonElement competitor)
    {
        var team = competitor.GetProperty("team");
        var record = Array(competitor, "record").FirstOrDefault(r => Str(r, "type") == "total");

        return new GameTeam(
            Str(team, "id") ?? "",
            Str(team, "abbreviation") ?? "",
            Str(team, "displayName") ?? "",
            Str(team, "shortDisplayName") ?? Str(team, "name") ?? Str(team, "abbreviation") ?? "",
            DarkLogo(team),
            record.ValueKind == JsonValueKind.Object ? Str(record, "displayValue") ?? Str(record, "summary") : null,
            int.TryParse(Str(competitor, "score"), out var score) ? score : null,
            Array(competitor, "linescores")
                .Select(l => int.TryParse(Str(l, "displayValue"), out var points) ? points : 0)
                .ToList(),
            Bool(competitor, "winner"),
            Bool(competitor, "possession"));
    }

    private static IReadOnlyList<string> PeriodLabels(int periods) =>
        Enumerable.Range(1, periods)
            .Select(p => p <= RegulationPeriods ? p.ToString(CultureInfo.InvariantCulture)
                : p == RegulationPeriods + 1 ? "OT" : $"OT{p - RegulationPeriods}")
            .ToList();

    private static GameInfo ParseInfo(JsonElement root, JsonElement competition)
    {
        string? venue = null, location = null, weather = null;
        int? attendance = null;
        var officials = new List<Official>();

        if (root.TryGetProperty("gameInfo", out var info))
        {
            if (info.TryGetProperty("venue", out var venueJson))
            {
                venue = Str(venueJson, "fullName");
                if (venueJson.TryGetProperty("address", out var address))
                {
                    var parts = new[] { Str(address, "city"), Str(address, "state") ?? Str(address, "country") }.OfType<string>().ToList();
                    location = parts.Count > 0 ? string.Join(", ", parts) : null;
                }
            }

            attendance = Int(info, "attendance") is int a and > 0 ? a : null;
            officials = Array(info, "officials")
                .Select(o => new Official(
                    Str(o, "displayName") ?? "",
                    o.TryGetProperty("position", out var pos) ? Str(pos, "displayName") ?? "" : ""))
                .Where(o => o.Name.Length > 0)
                .ToList();

            if (info.TryGetProperty("weather", out var w))
            {
                var parts = new List<string>();
                if (Int(w, "temperature") is int temp) parts.Add($"{temp}°");
                if (Int(w, "precipitation") is int precip and > 0) parts.Add($"{precip}% precip");
                if (Int(w, "gust") is int gust and > 0) parts.Add($"gusts {gust} mph");
                weather = parts.Count > 0 ? string.Join(" · ", parts) : null;
            }
        }

        var broadcast = string.Join(", ", Array(competition, "broadcasts")
            .Select(b => b.TryGetProperty("media", out var media) ? Str(media, "shortName") : null)
            .OfType<string>()
            .Distinct());

        return new GameInfo(venue, location, attendance, broadcast.Length > 0 ? broadcast : null, weather, officials);
    }

    private static List<TeamStatLine> ParseTeamStats(JsonElement root, string awayAbbreviation)
    {
        if (!root.TryGetProperty("boxscore", out var box))
        {
            return [];
        }

        var teams = Array(box, "teams").ToList();
        var awayJson = teams.FirstOrDefault(t => Str(t, "homeAway") == "away" || TeamAbbreviation(t) == awayAbbreviation);
        var homeJson = teams.FirstOrDefault(t => !t.Equals(awayJson) && (Str(t, "homeAway") == "home" || TeamAbbreviation(t) != awayAbbreviation));
        if (awayJson.ValueKind != JsonValueKind.Object || homeJson.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var homeStats = Array(homeJson, "statistics")
            .GroupBy(s => Str(s, "name") ?? "")
            .ToDictionary(g => g.Key, g => g.First());

        var lines = new List<TeamStatLine>();
        foreach (var stat in Array(awayJson, "statistics").DistinctBy(s => Str(s, "name")))
        {
            var key = Str(stat, "name") ?? "";
            if (!homeStats.TryGetValue(key, out var homeStat))
            {
                continue;
            }

            var awayValue = Str(stat, "displayValue") ?? "";
            var homeValue = Str(homeStat, "displayValue") ?? "";
            lines.Add(new TeamStatLine(
                key,
                Str(stat, "label") ?? key,
                awayValue,
                homeValue,
                StatNumber(key, awayValue),
                StatNumber(key, homeValue),
                LowerIsBetter.Contains(key)));
        }

        return lines;
    }

    /// <summary>
    /// A comparable number for a stat display value: plain numbers, "made-attempts" and "made/attempts"
    /// as a rate (3rd down "9-18" → 0.5), "count-yards" penalties/sacks by their count, and "mm:ss"
    /// possession as minutes.
    /// </summary>
    internal static double? StatNumber(string key, string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        if (value.Split(':') is [var min, var sec] && int.TryParse(min, out var m) && int.TryParse(sec, out var s))
        {
            return m + s / 60.0;
        }

        if (value.Split('-', '/') is [var first, var second]
            && double.TryParse(first, CultureInfo.InvariantCulture, out var made)
            && double.TryParse(second, CultureInfo.InvariantCulture, out var of))
        {
            if (CountThenYards.Contains(key))
            {
                return made;
            }

            return of == 0 ? 0 : made / of;
        }

        return null;
    }

    private static List<PlayerStatTable> ParsePlayerStats(JsonElement root)
    {
        if (!root.TryGetProperty("boxscore", out var box))
        {
            return [];
        }

        var tables = new List<PlayerStatTable>();
        foreach (var team in Array(box, "players"))
        {
            var abbreviation = TeamAbbreviation(team);
            foreach (var category in Array(team, "statistics"))
            {
                var name = Str(category, "name") ?? "";
                var players = Array(category, "athletes")
                    .Select(a =>
                    {
                        var athlete = a.GetProperty("athlete");
                        return new PlayerStatRow(
                            Str(athlete, "displayName") ?? "",
                            Str(athlete, "jersey"),
                            Array(a, "stats").Select(s => s.GetString() ?? "").ToList());
                    })
                    .ToList();

                if (players.Count == 0)
                {
                    continue;
                }

                var totals = Array(category, "totals").Select(t => t.GetString() ?? "").ToList();
                tables.Add(new PlayerStatTable(
                    abbreviation,
                    name,
                    CategoryTitles.GetValueOrDefault(name) ?? Str(category, "text") ?? name,
                    Array(category, "labels").Select(l => l.GetString() ?? "").ToList(),
                    players,
                    totals.Count > 0 ? totals : null));
            }
        }

        return tables
            .OrderBy(t => System.Array.IndexOf(CategoryOrder, t.Category) is var i and >= 0 ? i : CategoryOrder.Length)
            .ToList();
    }

    private static List<ScoringPlay> ParseScoringPlays(JsonElement root) =>
        Array(root, "scoringPlays")
            .Select(p =>
            {
                var team = p.TryGetProperty("team", out var t) ? t : default;
                var type = p.TryGetProperty("type", out var ty) ? ty : default;
                return new ScoringPlay(
                    Period(p),
                    Clock(p),
                    team.ValueKind == JsonValueKind.Object ? Str(team, "abbreviation") ?? "" : "",
                    team.ValueKind == JsonValueKind.Object ? DarkLogo(team) : null,
                    type.ValueKind == JsonValueKind.Object ? Str(type, "text") ?? "" : "",
                    type.ValueKind == JsonValueKind.Object ? Str(type, "abbreviation") ?? "" : "",
                    Str(p, "text") ?? "",
                    Int(p, "awayScore") ?? 0,
                    Int(p, "homeScore") ?? 0);
            })
            .ToList();

    private static List<Drive> ParseDrives(JsonElement root)
    {
        if (!root.TryGetProperty("drives", out var drivesJson))
        {
            return [];
        }

        // "previous" holds finished drives; during a live game "current" is the one on the field.
        var all = Array(drivesJson, "previous").ToList();
        if (drivesJson.TryGetProperty("current", out var current) && current.ValueKind == JsonValueKind.Object
            && !all.Any(d => Str(d, "id") == Str(current, "id")))
        {
            all.Add(current);
        }

        return all.Select(d =>
        {
            var team = d.TryGetProperty("team", out var t) ? t : default;
            var start = d.TryGetProperty("start", out var s) ? s : default;
            var end = d.TryGetProperty("end", out var e) ? e : default;

            return new Drive(
                team.ValueKind == JsonValueKind.Object ? Str(team, "abbreviation") ?? "" : "",
                team.ValueKind == JsonValueKind.Object ? DarkLogo(team) : null,
                Str(d, "description") ?? "",
                Str(d, "displayResult") ?? Str(d, "result") ?? "In progress",
                Bool(d, "isScore"),
                start.ValueKind == JsonValueKind.Object ? Period(start) : 0,
                start.ValueKind == JsonValueKind.Object ? Clock(start) : "",
                start.ValueKind == JsonValueKind.Object ? Str(start, "text") : null,
                end.ValueKind == JsonValueKind.Object ? Str(end, "text") : null,
                Int(d, "yards") ?? 0,
                Array(d, "plays").Select(ParsePlay).ToList());
        }).ToList();
    }

    private static Play ParsePlay(JsonElement p)
    {
        var start = p.TryGetProperty("start", out var s) ? s : default;
        var type = p.TryGetProperty("type", out var t) ? Str(t, "text") ?? "" : "";
        var isTurnover = Bool(p, "isTurnover");

        // ESPN tags timeouts and period breaks with the current down, but they aren't snaps.
        var fromScrimmage = start.ValueKind == JsonValueKind.Object && Int(start, "down") is > 0
            && !NonSnapTypes.Any(n => type.Contains(n, StringComparison.OrdinalIgnoreCase));

        return new Play(
            Str(p, "id") ?? "",
            Period(p),
            Clock(p),
            fromScrimmage ? Str(start, "downDistanceText") : null,
            type,
            Str(p, "text") ?? "",
            // On a turnover statYardage is the defense's return, not an offensive gain.
            fromScrimmage && !isTurnover ? Int(p, "statYardage") : null,
            Bool(p, "scoringPlay"),
            isTurnover,
            Bool(p, "isPenalty"),
            Int(p, "awayScore") ?? 0,
            Int(p, "homeScore") ?? 0);
    }

    private static List<WinProbabilityPoint> ParseWinProbability(JsonElement root, List<Drive> drives)
    {
        // Points only carry a play id; the drives tell us which quarter each play was in.
        var periods = drives.SelectMany(d => d.Plays).GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First().Period);
        var lastPeriod = 1;

        return Array(root, "winprobability")
            .Select(w =>
            {
                if (Str(w, "playId") is string id && periods.TryGetValue(id, out var period) && period > 0)
                {
                    lastPeriod = period;
                }

                var home = w.TryGetProperty("homeWinPercentage", out var h) && h.TryGetDouble(out var pct) ? pct : 0.5;
                return new WinProbabilityPoint(lastPeriod, Math.Clamp(home, 0, 1));
            })
            .ToList();
    }

    private static List<TeamLeaders> ParseLeaders(JsonElement root) =>
        Array(root, "leaders")
            .Select(t => new TeamLeaders(
                TeamAbbreviation(t),
                Array(t, "leaders")
                    .Select(category =>
                    {
                        var top = Array(category, "leaders").FirstOrDefault();
                        if (top.ValueKind != JsonValueKind.Object || !top.TryGetProperty("athlete", out var athlete))
                        {
                            return null;
                        }

                        return new Leader(
                            Str(category, "displayName") ?? Str(category, "name") ?? "",
                            Str(athlete, "displayName") ?? "",
                            athlete.TryGetProperty("position", out var pos) ? Str(pos, "abbreviation") : null,
                            Str(top, "displayValue") ?? "");
                    })
                    .OfType<Leader>()
                    .ToList()))
            .Where(t => t.Leaders.Count > 0)
            .ToList();

    private static List<TeamInjuries> ParseInjuries(JsonElement root) =>
        Array(root, "injuries")
            .Select(t => new TeamInjuries(
                TeamAbbreviation(t),
                Array(t, "injuries")
                    .Select(i =>
                    {
                        var athlete = i.TryGetProperty("athlete", out var a) ? a : default;
                        var details = i.TryGetProperty("details", out var d) ? d : default;
                        return new Injury(
                            athlete.ValueKind == JsonValueKind.Object ? Str(athlete, "displayName") ?? "" : "",
                            athlete.ValueKind == JsonValueKind.Object && athlete.TryGetProperty("position", out var pos) ? Str(pos, "abbreviation") : null,
                            Str(i, "status") ?? "",
                            details.ValueKind == JsonValueKind.Object ? Str(details, "type") : null);
                    })
                    .Where(i => i.Name.Length > 0)
                    .ToList()))
            .Where(t => t.Injuries.Count > 0)
            .ToList();

    private static Pregame? ParsePregame(JsonElement root, string awayId, string homeId)
    {
        double? homeChance = null, awayChance = null;
        if (root.TryGetProperty("predictor", out var predictor) && predictor.ValueKind == JsonValueKind.Object)
        {
            homeChance = Projection(predictor, "homeTeam");
            awayChance = Projection(predictor, "awayTeam");
        }

        BettingLine? line = null;
        var pick = Array(root, "pickcenter").FirstOrDefault();
        if (pick.ValueKind == JsonValueKind.Object)
        {
            line = new BettingLine(
                pick.TryGetProperty("provider", out var provider) ? Str(provider, "name") ?? "" : "",
                Str(pick, "details"),
                pick.TryGetProperty("overUnder", out var ou) && ou.TryGetDouble(out var total) ? total : null,
                MoneyLine(pick, "awayTeamOdds"),
                MoneyLine(pick, "homeTeamOdds"));
        }

        var form = Array(root, "lastFiveGames")
            .Select(t => new TeamForm(
                TeamAbbreviation(t),
                Array(t, "events")
                    .Select(e => new RecentGame(
                        Str(e, "gameResult") ?? "",
                        Str(e, "score") ?? "",
                        Str(e, "atVs") ?? "",
                        e.TryGetProperty("opponent", out var opp) ? Str(opp, "abbreviation") ?? "" : ""))
                    .ToList()))
            .ToList();

        return homeChance is null && line is null && form.Count == 0 ? null : new Pregame(homeChance, awayChance, line, form);
    }

    private static double? Projection(JsonElement predictor, string side) =>
        predictor.TryGetProperty(side, out var team)
        && double.TryParse(Str(team, "gameProjection"), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static int? MoneyLine(JsonElement pick, string side) =>
        pick.TryGetProperty(side, out var odds) && odds.TryGetProperty("moneyLine", out var ml) && ml.TryGetDouble(out var value)
            ? (int)value
            : null;

    private static string TeamAbbreviation(JsonElement withTeam) =>
        withTeam.TryGetProperty("team", out var team) ? Str(team, "abbreviation") ?? "" : "";

    private static int Period(JsonElement element) =>
        element.TryGetProperty("period", out var period) ? Int(period, "number") ?? 0 : 0;

    private static string Clock(JsonElement element) =>
        element.TryGetProperty("clock", out var clock) ? Str(clock, "displayValue") ?? "" : "";

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>Prefers ESPN's dark-background logo variant, since the app is dark-themed.</summary>
    private static string? DarkLogo(JsonElement team)
    {
        var logos = Array(team, "logos").ToList();
        var dark = logos.FirstOrDefault(l => Array(l, "rel").Any(r => r.GetString() == "dark"));
        if (dark.ValueKind == JsonValueKind.Object)
        {
            return Str(dark, "href");
        }

        return logos.Count > 0 ? Str(logos[0], "href") : Str(team, "logo")?.Replace("/500/", "/500-dark/");
    }
}
