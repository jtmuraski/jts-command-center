using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using JtsCommandCenter.Core.Sports;
using static JtsCommandCenter.Infrastructure.Sports.EspnJson;

namespace JtsCommandCenter.Infrastructure.Sports;

/// <summary>Weekly league scoreboards from ESPN's public site API (no API key).</summary>
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

        var cacheKey = week is null ? $"scoreboard:{league}:current" : $"scoreboard:{league}:{week.SeasonType}:{week.Week}";
        if (cache.TryGetValue(cacheKey, out WeekScoreboard? cached) && cached is not null)
        {
            return cached;
        }

        var url = $"apis/site/v2/sports/{leagueOptions.Sport}/{leagueOptions.League}/scoreboard"
            + (week is null ? "" : $"?seasontype={week.SeasonType}&week={week.Week}");

        logger.LogInformation("Fetching {League} scoreboard {Week} from ESPN", league, week?.Label ?? "(current)");

        using var doc = await http.GetJsonAsync(url, cancellationToken);
        var scoreboard = Parse(doc.RootElement);

        var ttl = scoreboard.HasLiveGame ? TimeSpan.FromSeconds(settings.LiveCacheSeconds)
            : scoreboard.IsComplete ? TimeSpan.FromMinutes(settings.CompletedWeekCacheMinutes)
            : TimeSpan.FromMinutes(settings.ScoreboardCacheMinutes);
        cache.Set(cacheKey, scoreboard, ttl);
        return scoreboard;
    }

    private WeekScoreboard Parse(JsonElement root)
    {
        var season = root.GetProperty("season");
        var seasonType = Int(season, "type") ?? 2;
        var weekNumber = root.TryGetProperty("week", out var weekJson) ? Int(weekJson, "number") ?? 1 : 1;

        var weeks = ParseCalendar(root);
        var week = weeks.FirstOrDefault(w => w.SeasonType == seasonType && w.Week == weekNumber)
            ?? new WeekRef(seasonType, weekNumber, $"Week {weekNumber}", default, default);

        var byes = weekJson.ValueKind == JsonValueKind.Object
            ? Array(weekJson, "teamsOnBye")
                .Select(t => new ByeTeam(Str(t, "abbreviation") ?? "", Str(t, "shortDisplayName") ?? Str(t, "displayName") ?? "", Str(t, "logoDark") ?? Str(t, "logo")))
                .ToList()
            : [];

        var games = Array(root, "events").Select(ParseGame).OfType<ScoreboardGame>().OrderBy(g => g.Start).ToList();

        return new WeekScoreboard(
            Int(season, "year") ?? DateTime.Now.Year,
            week,
            weeks,
            games,
            byes,
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
    }

    private static List<WeekRef> ParseCalendar(JsonElement root)
    {
        var weeks = new List<WeekRef>();
        var league = Array(root, "leagues").FirstOrDefault();
        if (league.ValueKind != JsonValueKind.Object)
        {
            return weeks;
        }

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

    // Calendar weeks run from ~07:00Z on the start day to ~06:59Z the day after the last day, so the end
    // instant is pulled back half a day to land on the week's actual last date.
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
        var record = Array(competitor, "records")
            .FirstOrDefault(r => Str(r, "type") == "total" || Str(r, "name") == "overall");

        return new ScoreboardTeam(
            Str(team, "abbreviation") ?? "",
            Str(team, "displayName") ?? "",
            Str(team, "shortDisplayName") ?? Str(team, "name") ?? "",
            // ESPN serves a dark-background variant of every scoreboard logo; the app is dark-themed.
            Str(team, "logo")?.Replace("/500/scoreboard/", "/500-dark/scoreboard/"),
            state is GameState.Live or GameState.Final ? ParseScore(competitor) : null,
            record.ValueKind == JsonValueKind.Object ? Str(record, "summary") : null,
            competitor.TryGetProperty("winner", out var winner) && winner.ValueKind == JsonValueKind.True);
    }
}
