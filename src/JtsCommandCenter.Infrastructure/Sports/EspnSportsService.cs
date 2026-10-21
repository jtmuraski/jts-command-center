using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using JtsCommandCenter.Core.Sports;
using static JtsCommandCenter.Infrastructure.Sports.EspnJson;

namespace JtsCommandCenter.Infrastructure.Sports;

/// <summary>
/// Team schedules from ESPN's public site API (site.api.espn.com, no API key). The API is undocumented
/// and its shapes vary a little by sport, so responses are read as loose JSON rather than fixed records.
/// </summary>
public sealed partial class EspnSportsService(
    HttpClient http,
    IMemoryCache cache,
    IOptions<SportsOptions> options,
    ILogger<EspnSportsService> logger) : ISportsService
{
    private const string CacheKey = "sports-report";
    private const int RegularSeason = 2;
    private const int Postseason = 3;

    private readonly SportsOptions settings = options.Value;
    private readonly TimeZoneInfo zone = EspnJson.ResolveZone(options.Value.TimeZone);

    public async Task<SportsReport> GetReportAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out SportsReport? cached) && cached is not null)
        {
            return cached;
        }

        var teams = await Task.WhenAll(settings.Teams.Select(t => LoadTeamOrPlaceholderAsync(t, cancellationToken)));
        if (teams.Length > 0 && teams.All(t => t.IsUnavailable))
        {
            throw new InvalidOperationException("No team schedules could be loaded from ESPN.");
        }

        var report = new SportsReport(teams, Now());
        var ttl = report.HasLiveGame || teams.Any(t => t.IsUnavailable)
            ? TimeSpan.FromSeconds(settings.LiveCacheSeconds)
            : TimeSpan.FromMinutes(settings.CacheMinutes);

        cache.Set(CacheKey, report, ttl);
        return report;
    }

    private async Task<TeamReport> LoadTeamOrPlaceholderAsync(TeamOptions team, CancellationToken cancellationToken)
    {
        try
        {
            return await LoadTeamAsync(team, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // One team failing shouldn't blank the whole card.
            logger.LogWarning(ex, "Failed to load {League} schedule for {Team}", team.League, team.Team);
            return new TeamReport
            {
                Label = team.Label,
                Display = team.Display,
                TeamName = team.Team.ToUpperInvariant(),
                ShortName = string.Empty,
                IsUnavailable = true
            };
        }
    }

    private async Task<TeamReport> LoadTeamAsync(TeamOptions team, CancellationToken cancellationToken)
    {
        var schedulePath = $"apis/site/v2/sports/{team.Sport}/{team.League}/teams/{team.Team}/schedule";

        logger.LogInformation("Fetching {League} schedule for {Team} from ESPN", team.League, team.Team);

        var regularTask = http.GetJsonAsync($"{schedulePath}?seasontype={RegularSeason}", cancellationToken);
        var postseasonTask = GetPostseasonAsync(schedulePath, cancellationToken);
        using var regular = await regularTask;
        using var postseason = await postseasonTask;

        var root = regular.RootElement;
        var teamJson = root.GetProperty("team");
        var teamId = Str(teamJson, "id") ?? team.Team;
        var standing = Str(teamJson, "standingSummary");

        var season = root.GetProperty("season");
        var seasonLabel = Str(season, "displayName") ?? Int(season, "year")?.ToString(CultureInfo.InvariantCulture) ?? "";
        var leagueSeasonType = Int(season, "type");

        var parsed = ParseGames(root, teamId)
            .Concat(postseason is null ? [] : ParseGames(postseason.RootElement, teamId))
            .DistinctBy(p => p.Game.Id)
            .OrderBy(p => p.Game.Start)
            .ToList();
        var games = parsed.Select(p => p.Game).ToList();

        // Record as of the latest regular-season game; ESPN's top-level summary can reset during the postseason.
        var record = parsed.LastOrDefault(p => p.Game.State == GameState.Final && !p.Game.IsPostseason)?.Record
            ?? Str(teamJson, "recordSummary");
        record = FormatRecord(record, team.AlwaysShowTies);

        var report = new TeamReport
        {
            Label = team.Label,
            Display = team.Display,
            TeamName = Str(teamJson, "displayName") ?? team.Team,
            ShortName = Str(teamJson, "name") ?? Str(teamJson, "abbreviation") ?? team.Team,
            Record = record,
            Standing = standing,
        };

        if (IsSeasonOver(games, leagueSeasonType))
        {
            var division = await FindDivisionNameAsync(team, teamId, Int(season, "year"), cancellationToken);
            return report with
            {
                Recap = new SeasonRecap(seasonLabel, record ?? "0-0", ParsePlace(standing), division ?? ParseDivision(standing))
            };
        }

        if (team.Display == SportsDisplay.Series)
        {
            var (current, next) = ScheduleMath.CurrentAndNextSeries(ScheduleMath.GroupSeries(games), DateOnly.FromDateTime(Now()));
            return report with { CurrentSeries = current, NextSeries = next };
        }

        return report with { LastGame = ScheduleMath.LastGame(games), NextGame = ScheduleMath.NextGame(games) };
    }

    /// <summary>
    /// Over means nothing left to play and at least one game played. While the league is in its postseason,
    /// a team whose last game was a playoff win is just waiting for its next round to be scheduled.
    /// </summary>
    private static bool IsSeasonOver(List<Game> games, int? leagueSeasonType)
    {
        if (games.Any(g => g.State is GameState.Live or GameState.Scheduled))
        {
            return false;
        }

        var last = ScheduleMath.LastGame(games);
        if (last is null)
        {
            return false;
        }

        var awaitingNextRound = leagueSeasonType == Postseason && last.IsPostseason && last.Result == GameResult.Win;
        return !awaitingNextRound;
    }

    private async Task<JsonDocument?> GetPostseasonAsync(string schedulePath, CancellationToken cancellationToken)
    {
        try
        {
            return await http.GetJsonAsync($"{schedulePath}?seasontype={Postseason}", cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // Not every league/season exposes a postseason schedule; treat it as "no playoff games".
            logger.LogDebug(ex, "No postseason schedule at {Path}", schedulePath);
            return null;
        }
    }

    /// <summary>Looks up the full division name ("American League Central") from the standings feed.</summary>
    private async Task<string?> FindDivisionNameAsync(TeamOptions team, string teamId, int? year, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"apis/v2/sports/{team.Sport}/{team.League}/standings?seasontype={RegularSeason}&level=3"
                + (year is int y ? $"&season={y}" : "");
            using var standings = await http.GetJsonAsync(url, cancellationToken);
            return FindGroupContaining(standings.RootElement, teamId);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException)
        {
            logger.LogWarning(ex, "Couldn't load {League} standings", team.League);
            return null;
        }
    }

    private static string? FindGroupContaining(JsonElement group, string teamId)
    {
        if (group.TryGetProperty("children", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                if (FindGroupContaining(child, teamId) is string name)
                {
                    return name;
                }
            }
        }

        if (group.TryGetProperty("standings", out var standings) && standings.TryGetProperty("entries", out var entries))
        {
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.TryGetProperty("team", out var t) && Str(t, "id") == teamId)
                {
                    return Str(group, "name");
                }
            }
        }

        return null;
    }

    private sealed record ParsedGame(Game Game, string? Record);

    private IEnumerable<ParsedGame> ParseGames(JsonElement root, string teamId)
    {
        if (!root.TryGetProperty("events", out var events))
        {
            yield break;
        }

        foreach (var ev in events.EnumerateArray())
        {
            if (!ev.TryGetProperty("competitions", out var competitions) || competitions.GetArrayLength() == 0)
            {
                continue;
            }

            var competition = competitions[0];
            var competitors = competition.GetProperty("competitors").EnumerateArray().ToList();
            var us = competitors.FirstOrDefault(c => Str(c, "id") == teamId);
            var them = competitors.FirstOrDefault(c => Str(c, "id") != teamId);
            if (us.ValueKind == JsonValueKind.Undefined || them.ValueKind == JsonValueKind.Undefined)
            {
                continue;
            }

            var status = competition.GetProperty("status").GetProperty("type");
            var state = EspnJson.ParseState(status);
            var start = DateTimeOffset.Parse(Str(ev, "date") ?? Str(competition, "date")!, CultureInfo.InvariantCulture);
            var timeValid = !competition.TryGetProperty("timeValid", out var tv) || tv.ValueKind != JsonValueKind.False;
            var seasonType = ev.TryGetProperty("seasonType", out var st) ? Int(st, "type") : null;
            var label = ev.TryGetProperty("week", out var week) ? Str(week, "text") : null;

            var game = new Game(
                Str(ev, "id") ?? Str(competition, "id") ?? start.ToString("O"),
                TimeZoneInfo.ConvertTime(start, zone).DateTime,
                !timeValid,
                state,
                Str(status, "shortDetail") ?? Str(status, "description") ?? "",
                Str(us, "homeAway") == "home",
                ParseOpponent(them.GetProperty("team")),
                state is GameState.Live or GameState.Final ? EspnJson.ParseScore(us) : null,
                state is GameState.Live or GameState.Final ? EspnJson.ParseScore(them) : null,
                seasonType == Postseason,
                label);

            yield return new ParsedGame(game, state == GameState.Final ? ParseRecord(us) : null);
        }
    }

    private static Opponent ParseOpponent(JsonElement team) => new(
        Str(team, "abbreviation") ?? "",
        Str(team, "displayName") ?? Str(team, "name") ?? "",
        PickLogo(team));

    /// <summary>Prefers ESPN's dark-background logo variant, since the app is dark-themed.</summary>
    private static string? PickLogo(JsonElement team)
    {
        if (team.TryGetProperty("logos", out var logos) && logos.ValueKind == JsonValueKind.Array)
        {
            string? fallback = null;
            foreach (var logo in logos.EnumerateArray())
            {
                var href = Str(logo, "href");
                fallback ??= href;
                if (logo.TryGetProperty("rel", out var rel) && rel.EnumerateArray().Any(r => r.GetString() == "dark"))
                {
                    return href;
                }
            }
            return fallback;
        }

        return Str(team, "logo");
    }

    private static string? ParseRecord(JsonElement competitor)
    {
        if (!competitor.TryGetProperty("record", out var records) || records.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var all = records.EnumerateArray().ToList();
        var total = all.FirstOrDefault(r => Str(r, "type") == "total");
        if (total.ValueKind == JsonValueKind.Undefined && all.Count > 0)
        {
            total = all[0];
        }

        return total.ValueKind == JsonValueKind.Undefined ? null : Str(total, "displayValue");
    }

    /// <summary>Strips extras like ", 14 PTS" and optionally pads a missing tie count ("10-7" → "10-7-0").</summary>
    private static string? FormatRecord(string? record, bool alwaysShowTies)
    {
        if (string.IsNullOrWhiteSpace(record))
        {
            return null;
        }

        var core = record.Split(',')[0].Trim();
        return alwaysShowTies && core.Count(c => c == '-') == 1 ? core + "-0" : core;
    }

    // "3rd in AL Cent" → "3rd"
    private static string? ParsePlace(string? standing) =>
        standing is not null && PlaceRegex().Match(standing) is { Success: true } m ? m.Groups[1].Value : null;

    // "3rd in AL Cent" → "AL Cent" (fallback when the standings feed is unavailable)
    private static string? ParseDivision(string? standing) =>
        standing is not null && PlaceRegex().Match(standing) is { Success: true } m ? m.Groups[2].Value : null;

    [GeneratedRegex(@"^(\d+(?:st|nd|rd|th))\s+in\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceRegex();

    private DateTime Now() => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime;

}
