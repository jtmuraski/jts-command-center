using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using JtsCommandCenter.Core.Sports;
using static JtsCommandCenter.Infrastructure.Sports.EspnJson;

namespace JtsCommandCenter.Infrastructure.Sports;

/// <summary>
/// League scoreboards from ESPN's public site API (no API key). Football pages by week; leagues whose
/// calendar ESPN publishes as days (baseball, hockey) page by day.
/// </summary>
public sealed class EspnScoreboardService(
    HttpClient http,
    IMemoryCache cache,
    IOptions<SportsCenterOptions> options,
    IOptions<SportsOptions> sportsOptions,
    ILogger<EspnScoreboardService> logger) : IScoreboardService
{
    // Paging covers the regular season and postseason; preseason and off-season are skipped.
    private static readonly HashSet<int> PagedSeasonTypes = [2, 3];

    private readonly SportsCenterOptions settings = options.Value;
    private readonly TimeZoneInfo zone = ResolveZone(sportsOptions.Value.TimeZone);

    public async Task<WeekScoreboard> GetWeekAsync(string league, WeekRef? week = null, CancellationToken cancellationToken = default)
    {
        if (!settings.Leagues.TryGetValue(league, out var leagueOptions))
        {
            throw new InvalidOperationException($"No SportsCenter settings for league '{league}'.");
        }

        var cacheKey = week is null ? $"scoreboard:{league}:current"
            : week.IsDay ? $"scoreboard:{league}:{week.Start:yyyyMMdd}"
            : $"scoreboard:{league}:{week.SeasonType}:{week.Week}";
        if (cache.TryGetValue(cacheKey, out WeekScoreboard? cached) && cached is not null)
        {
            return cached;
        }

        var url = $"apis/site/v2/sports/{leagueOptions.Sport}/{leagueOptions.League}/scoreboard"
            + (week is null ? ""
                : week.IsDay ? $"?dates={week.Start:yyyyMMdd}"
                : $"?seasontype={week.SeasonType}&week={week.Week}");

        logger.LogInformation("Fetching {League} scoreboard {Week} from ESPN", league,
            week is null ? "(current)" : week.IsDay ? week.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : week.Label);

        using var doc = await http.GetJsonAsync(url, cancellationToken);
        var scoreboard = Parse(doc.RootElement, week);

        var ttl = scoreboard.HasLiveGame ? TimeSpan.FromSeconds(settings.LiveCacheSeconds)
            : scoreboard.IsComplete ? TimeSpan.FromMinutes(settings.CompletedWeekCacheMinutes)
            : TimeSpan.FromMinutes(settings.ScoreboardCacheMinutes);
        cache.Set(cacheKey, scoreboard, ttl);
        return scoreboard;
    }

    private WeekScoreboard Parse(JsonElement root, WeekRef? requested)
    {
        var league = Array(root, "leagues").FirstOrDefault();
        var hasLeague = league.ValueKind == JsonValueKind.Object;

        // The current scoreboard has "season" at the top; a dated request only has it on the league.
        var season = root.TryGetProperty("season", out var rootSeason) ? rootSeason
            : hasLeague && league.TryGetProperty("season", out var leagueSeason) ? leagueSeason
            : default;
        var hasSeason = season.ValueKind == JsonValueKind.Object;
        var hasWeek = root.TryGetProperty("week", out var weekJson) && weekJson.ValueKind == JsonValueKind.Object;

        WeekRef week;
        List<WeekRef> weeks;
        if (hasLeague && Str(league, "calendarType") == "day")
        {
            var day = requested?.Start
                ?? (root.TryGetProperty("day", out var dayJson) && DateOnly.TryParse(Str(dayJson, "date"), CultureInfo.InvariantCulture, out var espnDay)
                    ? espnDay
                    : DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime));
            week = WeekRef.ForDay(day);
            weeks = ParseDayCalendar(league, day);
        }
        else
        {
            var seasonType = (hasSeason ? Int(season, "type") : null) ?? requested?.SeasonType ?? 2;
            var weekNumber = (hasWeek ? Int(weekJson, "number") : null) ?? requested?.Week ?? 1;
            weeks = hasLeague ? ParseCalendar(league) : [];
            week = weeks.FirstOrDefault(w => w.SeasonType == seasonType && w.Week == weekNumber)
                ?? new WeekRef(seasonType, weekNumber, $"Week {weekNumber}", default, default);
        }

        var byes = hasWeek
            ? Array(weekJson, "teamsOnBye")
                .Select(t => new ByeTeam(Str(t, "abbreviation") ?? "", Str(t, "shortDisplayName") ?? Str(t, "displayName") ?? "", Str(t, "logoDark") ?? Str(t, "logo")))
                .ToList()
            : [];

        var games = Array(root, "events").Select(ParseGame).OfType<ScoreboardGame>().OrderBy(g => g.Start).ToList();

        return new WeekScoreboard(
            (hasSeason ? Int(season, "year") : null) ?? DateTime.Now.Year,
            week,
            weeks,
            games,
            byes,
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
    }

    private static List<WeekRef> ParseCalendar(JsonElement league)
    {
        var weeks = new List<WeekRef>();
        foreach (var period in Array(league, "calendar"))
        {
            if (!int.TryParse(Str(period, "value"), out var seasonType) || !PagedSeasonTypes.Contains(seasonType))
            {
                continue;
            }

            foreach (var entry in Array(period, "entries"))
            {
                if (!int.TryParse(Str(entry, "value"), out var number))
                {
                    continue;
                }

                weeks.Add(new WeekRef(
                    seasonType,
                    number,
                    Str(entry, "label") ?? $"Week {number}",
                    ParseDay(Str(entry, "startDate"), TimeSpan.Zero),
                    ParseDay(Str(entry, "endDate"), TimeSpan.FromHours(-12))));
            }
        }

        return weeks;
    }

    /// <summary>
    /// Game days for a daily league. ESPN lists either the days that have games (a whitelist, as for hockey)
    /// or, between the calendar's start and end, the days that don't (baseball's off days and All-Star break).
    /// </summary>
    private static List<WeekRef> ParseDayCalendar(JsonElement league, DateOnly current)
    {
        var listed = Array(league, "calendar")
            .Where(c => c.ValueKind == JsonValueKind.String)
            .Select(c => ParseDay(c.GetString(), TimeSpan.Zero))
            .Where(d => d != default)
            .ToHashSet();

        IEnumerable<DateOnly> days = listed;
        if (league.TryGetProperty("calendarIsWhitelist", out var whitelist) && whitelist.ValueKind == JsonValueKind.False)
        {
            var first = ParseDay(Str(league, "calendarStartDate"), TimeSpan.Zero);
            var last = ParseDay(Str(league, "calendarEndDate"), TimeSpan.FromHours(-12));
            days = first == default || last < first
                ? []
                : Enumerable.Range(0, last.DayNumber - first.DayNumber + 1).Select(first.AddDays).Where(d => !listed.Contains(d));
        }

        // The day being shown is always on the list, even when ESPN's calendar hasn't caught up (e.g. the playoffs).
        return days.Append(current).Distinct().Order().Select(WeekRef.ForDay).ToList();
    }

    // Calendar weeks and days run from ~07:00Z on the start day to ~06:59Z the day after the last day, so the
    // end instant is pulled back half a day to land on the actual last date.
    private static DateOnly ParseDay(string? value, TimeSpan adjust) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
            ? DateOnly.FromDateTime(parsed.UtcDateTime.Add(adjust))
            : default;

    private ScoreboardGame? ParseGame(JsonElement ev)
    {
        var competition = Array(ev, "competitions").FirstOrDefault();
        if (competition.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var competitors = Array(competition, "competitors").ToList();
        var home = competitors.FirstOrDefault(c => Str(c, "homeAway") == "home");
        var away = competitors.FirstOrDefault(c => Str(c, "homeAway") == "away");
        if (home.ValueKind != JsonValueKind.Object || away.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var status = competition.GetProperty("status").GetProperty("type");
        var state = ParseState(status);
        var start = DateTimeOffset.Parse(Str(ev, "date") ?? Str(competition, "date")!, CultureInfo.InvariantCulture);
        var timeValid = !competition.TryGetProperty("timeValid", out var tv) || tv.ValueKind != JsonValueKind.False;

        string? venue = null, location = null;
        if (competition.TryGetProperty("venue", out var venueJson))
        {
            venue = Str(venueJson, "fullName");
            if (venueJson.TryGetProperty("address", out var address))
            {
                var parts = new[] { Str(address, "city"), Str(address, "state") ?? Str(address, "country") }.OfType<string>();
                location = string.Join(", ", parts);
            }
        }

        var broadcast = string.Join(", ", Array(competition, "broadcasts")
            .SelectMany(b => Array(b, "names"))
            .Select(n => n.GetString())
            .OfType<string>()
            .Distinct());

        var note = Array(competition, "notes").Select(n => Str(n, "headline")).FirstOrDefault(h => !string.IsNullOrWhiteSpace(h));

        return new ScoreboardGame(
            Str(ev, "id") ?? start.ToString("O"),
            TimeZoneInfo.ConvertTime(start, zone).DateTime,
            !timeValid,
            state,
            Str(status, "shortDetail") ?? Str(status, "description") ?? "",
            ParseTeam(away, state),
            ParseTeam(home, state),
            venue,
            string.IsNullOrEmpty(location) ? null : location,
            broadcast.Length == 0 ? null : broadcast,
            note);
    }

    private static ScoreboardTeam ParseTeam(JsonElement competitor, GameState state)
    {
        var team = competitor.GetProperty("team");
        // The overall record: "total" for football and baseball, "ytd" for hockey.
        var record = Array(competitor, "records")
            .FirstOrDefault(r => Str(r, "type") is "total" or "ytd" || Str(r, "name") == "overall");

        return new ScoreboardTeam(
            Str(team, "abbreviation") ?? "",
            Str(team, "displayName") ?? "",
            Str(team, "shortDisplayName") ?? Str(team, "name") ?? "",
            // ESPN serves a dark-background variant of every scoreboard logo; the app is dark-themed.
            Str(team, "logo")?.Replace("/500/scoreboard/", "/500-dark/scoreboard/"),
            state is GameState.Live or GameState.Final ? ParseScore(competitor) : null,
            record.ValueKind == JsonValueKind.Object ? Str(record, "summary") : null,
            competitor.TryGetProperty("winner", out var winner) && winner.ValueKind == JsonValueKind.True,
            state == GameState.Scheduled ? Probable(competitor) : null);
    }

    /// <summary>Baseball's probable starting pitcher, e.g. "J. Ryan (12-9, 3.42)".</summary>
    private static string? Probable(JsonElement competitor)
    {
        var probable = Array(competitor, "probables").FirstOrDefault();
        if (probable.ValueKind != JsonValueKind.Object || !probable.TryGetProperty("athlete", out var athlete))
        {
            return null;
        }

        var name = Str(athlete, "shortName") ?? Str(athlete, "displayName");
        return name is not null && Str(probable, "record") is string record ? $"{name} {record}" : name;
    }
}
