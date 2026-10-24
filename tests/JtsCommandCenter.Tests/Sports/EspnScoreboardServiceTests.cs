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
        options.Leagues["mlb"] = new LeagueOptions { Name = "MLB", Sport = "baseball", League = "mlb" };
        options.Leagues["nhl"] = new LeagueOptions { Name = "NHL", Sport = "hockey", League = "nhl" };

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

    // ---- Leagues that page by day (baseball, hockey) ------------------------------------------------

    private static string ProbableCompetitor(string homeAway, string abbreviation, string pitcher, string record) => $$"""
        {
          "homeAway": "{{homeAway}}",
          "team": { "abbreviation": "{{abbreviation}}", "displayName": "{{abbreviation}} Team", "shortDisplayName": "{{abbreviation}}" },
          "probables": [ { "athlete": { "shortName": "{{pitcher}}" }, "record": "{{record}}" } ]
        }
        """;

    private static string BaseballGame(string id, string date) => $$"""
        {
          "id": "{{id}}",
          "date": "{{date}}",
          "competitions": [ {
            "notes": [ { "headline": "World Series - Game 1" } ],
            "status": { "type": { "state": "pre", "completed": false, "shortDetail": "10/23 - 8:00 PM EDT" } },
            "competitors": [
              {{ProbableCompetitor("home", "MIL", "J. Misiorowski", "(0-0, 3.24)")}},
              {{ProbableCompetitor("away", "TB", "N. Martinez", "(1-0, 3.00)")}}
            ]
          } ]
        }
        """;

    // A dated request has no top-level "season" or "day"; the current one has both.
    private const string CurrentDay = """ "season": { "type": 3, "year": 2026 }, "day": { "date": "2026-10-23" }, """;

    private static string Day(string calendar, bool current, params string[] games) => $$"""
        {
          {{(current ? CurrentDay : "")}}
          "leagues": [ {
            "season": { "year": 2026, "type": { "type": 3 } },
            {{calendar}}
          } ],
          "events": [ {{string.Join(",", games)}} ]
        }
        """;

    // Baseball lists the days without games between the calendar's start and end.
    private const string BaseballCalendar = """
        "calendarType": "day", "calendarIsWhitelist": false,
        "calendarStartDate": "2026-10-20T07:00Z", "calendarEndDate": "2026-10-27T06:59Z",
        "calendar": [ "2026-10-22T07:00Z", "2026-10-25T07:00Z" ]
        """;

    // Hockey lists the days that have games.
    private static string HockeyGame(string id, string date) => $$"""
        {
          "id": "{{id}}",
          "date": "{{date}}",
          "competitions": [ {
            "status": { "type": { "state": "post", "completed": true, "shortDetail": "Final" } },
            "competitors": [
              { "homeAway": "home", "score": "2", "winner": true, "team": { "abbreviation": "CGY", "shortDisplayName": "Flames" },
                "records": [ { "type": "ytd", "summary": "3-6-0" }, { "type": "home", "summary": "2-3-0" } ] },
              { "homeAway": "away", "score": "1", "team": { "abbreviation": "MIN", "shortDisplayName": "Wild" },
                "records": [ { "type": "ytd", "summary": "4-4-2" } ] }
            ]
          } ]
        }
        """;

    private const string HockeyCalendar = """
        "calendarType": "day", "calendarIsWhitelist": true,
        "calendar": [ "2026-10-20T07:00Z", "2026-10-22T07:00Z", "2026-10-24T07:00Z" ]
        """;

    [Fact]
    public async Task DailyLeague_CurrentDayPagesThroughGameDays()
    {
        var handler = new FakeHttpHandler().Json("baseball/mlb/scoreboard", Day(BaseballCalendar, current: true,
            BaseballGame("401908042", "2026-10-24T00:00Z")));

        var day = await CreateService(handler).GetWeekAsync("mlb");

        Assert.Equal(WeekRef.ForDay(new DateOnly(2026, 10, 23)), day.Week);
        Assert.True(day.Week.IsDay);
        Assert.Equal(2026, day.Season);

        // Every day from the 20th through the 26th except the two listed off days.
        Assert.Equal([20, 21, 23, 24, 26], day.Weeks.Select(w => w.Start.Day));
        Assert.Equal(new DateOnly(2026, 10, 21), day.Previous!.Start);
        Assert.Equal(new DateOnly(2026, 10, 24), day.Next!.Start);

        var game = Assert.Single(day.Games);
        Assert.Equal(new DateTime(2026, 10, 23, 19, 0, 0), game.Start);
        Assert.Equal("World Series - Game 1", game.Note);
        Assert.Equal("N. Martinez (1-0, 3.00)", game.Away.Probable);
        Assert.Equal("J. Misiorowski (0-0, 3.24)", game.Home.Probable);
        Assert.Empty(day.TeamsOnBye);
    }

    [Fact]
    public async Task DailyLeague_RequestedDayUsesDatesAndIsCachedByDay()
    {
        var handler = new FakeHttpHandler().Json("hockey/nhl/scoreboard?dates=20261022", Day(HockeyCalendar, current: false,
            HockeyGame("401892560", "2026-10-23T01:00Z")));
        var service = CreateService(handler);

        var day = await service.GetWeekAsync("nhl", WeekRef.ForDay(new DateOnly(2026, 10, 22)));
        await service.GetWeekAsync("nhl", WeekRef.ForDay(new DateOnly(2026, 10, 22)));

        Assert.Single(handler.Requests);
        Assert.Equal(new DateOnly(2026, 10, 22), day.Week.Start);
        Assert.Equal(2026, day.Season); // from the league, since a dated response has no top-level season
        Assert.Equal([20, 22, 24], day.Weeks.Select(w => w.Start.Day));
        Assert.Equal(new DateOnly(2026, 10, 20), day.Previous!.Start);

        // Hockey's overall record is typed "ytd".
        var game = Assert.Single(day.Games);
        Assert.Equal(("4-4-2", "3-6-0"), (game.Away.Record, game.Home.Record));
        Assert.Equal((1, 2), (game.Away.Score, game.Home.Score));
        Assert.Null(game.Home.Probable);
        Assert.True(day.IsComplete);
    }

    [Fact]
    public async Task DailyLeague_ShownDayIsPageableEvenWhenMissingFromCalendar()
    {
        var handler = new FakeHttpHandler().Json("dates=20261023", Day(HockeyCalendar, current: false));

        var day = await CreateService(handler).GetWeekAsync("nhl", WeekRef.ForDay(new DateOnly(2026, 10, 23)));

        Assert.Equal([20, 22, 23, 24], day.Weeks.Select(w => w.Start.Day));
        Assert.Equal(new DateOnly(2026, 10, 24), day.Next!.Start);
    }
}
