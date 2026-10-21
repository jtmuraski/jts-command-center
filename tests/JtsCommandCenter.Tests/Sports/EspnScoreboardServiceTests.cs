using JtsCommandCenter.Core.Sports;
using JtsCommandCenter.Infrastructure.Sports;
using JtsCommandCenter.Tests.Support;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JtsCommandCenter.Tests.Sports;

public class EspnScoreboardServiceTests
{
    private const string Scoreboard = "football/nfl/scoreboard";

    private static EspnScoreboardService CreateService(FakeHttpHandler handler)
    {
        var options = new SportsCenterOptions();
        options.Leagues["nfl"] = new LeagueOptions { Name = "NFL", Sport = "football", League = "nfl" };

        return new EspnScoreboardService(
            handler.CreateClient(),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(options),
            Options.Create(new SportsOptions { TimeZone = "America/Chicago" }),
            NullLogger<EspnScoreboardService>.Instance);
    }

    private static string Competitor(string homeAway, string abbreviation, string name, string? score, bool? winner, string record) => $$"""
        {
          "homeAway": "{{homeAway}}",
          {{(winner is bool w ? $"\"winner\": {(w ? "true" : "false")}," : "")}}
          {{(score is null ? "" : $"\"score\": \"{score}\",")}}
          "records": [ { "type": "total", "summary": "{{record}}" } ],
          "team": { "abbreviation": "{{abbreviation}}", "displayName": "{{name}}", "shortDisplayName": "{{name}}",
                    "logo": "https://a.espncdn.com/i/teamlogos/nfl/500/scoreboard/{{abbreviation.ToLowerInvariant()}}.png" }
        }
        """;

    private static string Game(string id, string date, bool completed, string state, string away, string home, string? awayScore = null, string? homeScore = null) => $$"""
        {
          "id": "{{id}}",
          "date": "{{date}}",
          "competitions": [ {
            "timeValid": true,
            "venue": { "fullName": "U.S. Bank Stadium", "address": { "city": "Minneapolis", "state": "MN" } },
            "broadcasts": [ { "names": [ "CBS" ] } ],
            "notes": [],
            "status": { "type": { "state": "{{state}}", "completed": {{(completed ? "true" : "false")}}, "shortDetail": "{{(completed ? "Final" : "10/25 - 1:00 PM EDT")}}" } },
            "competitors": [
              {{Competitor("home", home, home + " Team", homeScore, completed ? int.Parse(homeScore!) > int.Parse(awayScore!) : null, "5-0")}},
              {{Competitor("away", away, away + " Team", awayScore, completed ? int.Parse(awayScore!) > int.Parse(homeScore!) : null, "2-4")}}
            ]
          } ]
        }
        """;

    private static string Week(int seasonType, int week, params string[] games) => $$"""
        {
          "season": { "type": {{seasonType}}, "year": 2026 },
          "week": { "number": {{week}}, "teamsOnBye": [
            { "abbreviation": "BUF", "shortDisplayName": "Bills", "logoDark": "https://a.espncdn.com/i/teamlogos/nfl/500-dark/scoreboard/buf.png" }
          ] },
          "leagues": [ { "calendar": [
            { "label": "Preseason", "value": "1", "entries": [ { "label": "Preseason Week 1", "value": "2", "startDate": "2026-08-13T07:00Z", "endDate": "2026-08-20T06:59Z" } ] },
            { "label": "Regular Season", "value": "2", "entries": [
              { "label": "Week 6", "value": "6", "startDate": "2026-10-14T07:00Z", "endDate": "2026-10-21T06:59Z" },
              { "label": "Week 7", "value": "7", "startDate": "2026-10-21T07:00Z", "endDate": "2026-10-28T06:59Z" }
            ] },
            { "label": "Postseason", "value": "3", "entries": [
              { "label": "Wild Card", "value": "1", "startDate": "2027-01-13T08:00Z", "endDate": "2027-01-20T07:59Z" }
            ] },
            { "label": "Off Season", "value": "4", "entries": [] }
          ] } ],
          "events": [ {{string.Join(",", games)}} ]
        }
        """;

    [Fact]
    public async Task CurrentWeek_ParsesCalendarGamesAndByes()
    {
        var handler = new FakeHttpHandler().Json(Scoreboard, Week(2, 7,
            Game("2", "2026-10-25T17:00Z", completed: false, "pre", "IND", "MIN"),
            Game("1", "2026-10-23T00:15Z", completed: false, "pre", "NE", "CHI")));

        var week = await CreateService(handler).GetWeekAsync("nfl");

        // Preseason and off-season are left out of paging.
        Assert.Equal(["Week 6", "Week 7", "Wild Card"], week.Weeks.Select(w => w.Label));
        Assert.Equal(new WeekRef(2, 7, "Week 7", new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 27)), week.Week);
        Assert.Equal(new DateOnly(2027, 1, 19), week.Weeks[2].End);
        Assert.Equal(week.Weeks[0], week.Previous);
        Assert.Equal(week.Weeks[2], week.Next);

        // Sorted by kickoff: Thursday night first.
        Assert.Equal(["1", "2"], week.Games.Select(g => g.Id));
        var colts = week.Games[1];
        Assert.Equal(new DateTime(2026, 10, 25, 12, 0, 0), colts.Start);
        Assert.Equal(("IND", "MIN"), (colts.Away.Abbreviation, colts.Home.Abbreviation));
        Assert.Equal("U.S. Bank Stadium", colts.Venue);
        Assert.Equal("Minneapolis, MN", colts.Location);
        Assert.Equal("CBS", colts.Broadcast);
        Assert.Null(colts.Home.Score);
        Assert.Contains("/500-dark/scoreboard/", colts.Home.LogoUrl);
        Assert.True(colts.Involves(["min", "CIN"]));
        Assert.False(colts.Involves(["CIN"]));

        Assert.Equal("BUF", Assert.Single(week.TeamsOnBye).Abbreviation);
        Assert.False(week.IsComplete);
    }

    [Fact]
    public async Task CompletedWeek_HasScoresAndWinners()
    {
        var handler = new FakeHttpHandler().Json($"{Scoreboard}?seasontype=2&week=6", Week(2, 6,
            Game("9", "2026-10-11T17:00Z", completed: true, "post", "MIN", "NO", awayScore: "23", homeScore: "13")));

        var week = await CreateService(handler).GetWeekAsync("nfl", new WeekRef(2, 6, "Week 6", default, default));

        var game = Assert.Single(week.Games);
        Assert.Equal(GameState.Final, game.State);
        Assert.Equal((23, 13), (game.Away.Score, game.Home.Score));
        Assert.True(game.Away.IsWinner);
        Assert.False(game.Home.IsWinner);
        Assert.True(week.IsComplete);
        Assert.Null(week.Previous);
    }

    [Fact]
    public async Task RequestedWeek_IsCachedSeparatelyFromCurrent()
    {
        var handler = new FakeHttpHandler()
            .Json("week=6", Week(2, 6, Game("9", "2026-10-11T17:00Z", completed: true, "post", "MIN", "NO", "23", "13")))
            .Json(Scoreboard, Week(2, 7, Game("2", "2026-10-25T17:00Z", completed: false, "pre", "IND", "MIN")));
        var service = CreateService(handler);

        var current = await service.GetWeekAsync("nfl");
        var previous = await service.GetWeekAsync("nfl", current.Previous);
        await service.GetWeekAsync("nfl", current.Previous);

        Assert.Equal(7, current.Week.Week);
        Assert.Equal(6, previous.Week.Week);
        Assert.Equal(2, handler.Requests.Count);
    }
}
