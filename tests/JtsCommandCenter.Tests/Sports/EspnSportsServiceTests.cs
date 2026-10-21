using JtsCommandCenter.Core.Sports;
using JtsCommandCenter.Infrastructure.Sports;
using JtsCommandCenter.Tests.Support;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static JtsCommandCenter.Tests.Support.EspnFixtures;

namespace JtsCommandCenter.Tests.Sports;

public class EspnSportsServiceTests
{
    private const string TwinsSchedule = "baseball/mlb/teams/min/schedule";
    private const string VikingsSchedule = "football/nfl/teams/min/schedule";

    private static readonly string Twins = Team("9", "MIN", "Minnesota Twins", "Twins");
    private static readonly string Vikings = Team("16", "MIN", "Minnesota Vikings", "Vikings");

    private static readonly TeamOptions TwinsOptions = new()
    {
        Label = "Baseball", Sport = "baseball", League = "mlb", Team = "min", Display = SportsDisplay.Series
    };

    private static readonly TeamOptions VikingsOptions = new()
    {
        Label = "Football", Sport = "football", League = "nfl", Team = "min", AlwaysShowTies = true
    };

    private static EspnSportsService CreateService(FakeHttpHandler handler, params TeamOptions[] teams) => new(
        handler.CreateClient(),
        new MemoryCache(new MemoryCacheOptions()),
        Options.Create(new SportsOptions { TimeZone = "America/Chicago", Teams = [.. teams] }),
        NullLogger<EspnSportsService>.Instance);

    [Fact]
    public async Task SeasonOver_ReturnsRecapWithFullDivisionName()
    {
        var handler = new FakeHttpHandler()
            .Json($"{TwinsSchedule}?seasontype=2", Schedule(Twins, 2026, 4, "2026", "3rd in AL Cent",
                Event("1", "2026-09-26T19:10Z", "9", "13", "TEX", home: true, "post", completed: true, 3, 4, ourRecord: "76-85"),
                Event("2", "2026-09-27T19:10Z", "9", "13", "TEX", home: true, "post", completed: true, 6, 4, ourRecord: "77-85")))
            .Json("baseball/mlb/standings", """
                { "children": [ { "name": "American League", "children": [
                  { "name": "American League East", "standings": { "entries": [ { "team": { "id": "1" } } ] } },
                  { "name": "American League Central", "standings": { "entries": [ { "team": { "id": "9" } } ] } }
                ] } ] }
                """);
        // No postseason rule: the 404 is treated as "no playoff games".

        var report = await CreateService(handler, TwinsOptions).GetReportAsync();

        var twins = Assert.Single(report.Teams);
        Assert.Equal(new SeasonRecap("2026", "77-85", "3rd", "American League Central"), twins.Recap);
        Assert.Equal("Minnesota Twins", twins.TeamName);
        Assert.Null(twins.CurrentSeries);
    }

    [Fact]
    public async Task SeasonOver_StandingsUnavailable_FallsBackToSummaryDivision()
    {
        var handler = new FakeHttpHandler()
            .Json($"{TwinsSchedule}?seasontype=2", Schedule(Twins, 2026, 4, "2026", "3rd in AL Cent",
                Event("1", "2026-09-27T19:10Z", "9", "13", "TEX", home: true, "post", completed: true, 6, 4, ourRecord: "77-85")))
            .Fail("standings");

        var report = await CreateService(handler, TwinsOptions).GetReportAsync();

        Assert.Equal("AL Cent", report.Teams[0].Recap?.Division);
    }

    [Fact]
    public async Task InSeason_ReturnsLastAndNextGame()
    {
        var handler = new FakeHttpHandler()
            .Json($"{VikingsSchedule}?seasontype=2", Schedule(Vikings, 2026, 2, "2026", "1st in NFC North",
                Event("1", "2026-10-11T17:00Z", "16", "18", "NO", home: false, "post", completed: true, 23, 13, ourRecord: "5-0", week: "Week 5"),
                Event("2", "2026-10-25T17:00Z", "16", "11", "IND", home: true, "pre", completed: false, week: "Week 7")));

        var report = await CreateService(handler, VikingsOptions).GetReportAsync();

        var vikings = report.Teams[0];
        Assert.Null(vikings.Recap);
        Assert.Equal("5-0-0", vikings.Record); // AlwaysShowTies pads the missing tie count
        Assert.Equal("1st in NFC North", vikings.Standing);

        Assert.Equal("NO", vikings.LastGame?.Opponent.Abbreviation);
        Assert.Equal(GameResult.Win, vikings.LastGame?.Result);
        Assert.False(vikings.LastGame?.IsHome);
        Assert.Equal("Week 5", vikings.LastGame?.Label);

        Assert.Equal("IND", vikings.NextGame?.Opponent.Abbreviation);
        Assert.Equal(GameState.Scheduled, vikings.NextGame?.State);
        Assert.Null(vikings.NextGame?.Score);
        // 17:00Z on Oct 25 is noon Central (CDT).
        Assert.Equal(new DateTime(2026, 10, 25, 12, 0, 0), vikings.NextGame?.Start);
    }

    [Fact]
    public async Task PlayoffWinAwaitingNextRound_IsNotSeasonOver()
    {
        var handler = new FakeHttpHandler()
            .Json($"{VikingsSchedule}?seasontype=2", Schedule(Vikings, 2026, 3, "2026", "1st in NFC North",
                Event("1", "2027-01-10T18:00Z", "16", "3", "CHI", home: true, "post", completed: true, 30, 10, ourRecord: "14-3")))
            .Json($"{VikingsSchedule}?seasontype=3", Schedule(Vikings, 2026, 3, "2026", null,
                Event("2", "2027-01-17T18:00Z", "16", "8", "DET", home: true, "post", completed: true, 24, 20, seasonType: 3)));

        var report = await CreateService(handler, VikingsOptions).GetReportAsync();

        Assert.Null(report.Teams[0].Recap);
        Assert.Equal("DET", report.Teams[0].LastGame?.Opponent.Abbreviation);
        Assert.True(report.Teams[0].LastGame?.IsPostseason);
        Assert.Null(report.Teams[0].NextGame);
    }

    [Fact]
    public async Task PlayoffLoss_EndsSeason_WithRegularSeasonRecord()
    {
        var handler = new FakeHttpHandler()
            .Json($"{VikingsSchedule}?seasontype=2", Schedule(Vikings, 2026, 3, "2026", "1st in NFC North",
                Event("1", "2027-01-10T18:00Z", "16", "3", "CHI", home: true, "post", completed: true, 30, 10, ourRecord: "14-3")))
            .Json($"{VikingsSchedule}?seasontype=3", Schedule(Vikings, 2026, 3, "2026", null,
                Event("2", "2027-01-17T18:00Z", "16", "8", "DET", home: true, "post", completed: true, 20, 24, seasonType: 3, ourRecord: "0-1")));

        var report = await CreateService(handler, VikingsOptions).GetReportAsync();

        Assert.Equal("14-3-0", report.Teams[0].Recap?.Record);
    }

    [Fact]
    public async Task OneTeamFails_OthersStillLoad()
    {
        var handler = new FakeHttpHandler()
            .Json($"{VikingsSchedule}?seasontype=2", Schedule(Vikings, 2026, 2, "2026", null,
                Event("2", "2026-10-25T17:00Z", "16", "11", "IND", home: true, "pre", completed: false)))
            .Fail(TwinsSchedule);

        var report = await CreateService(handler, VikingsOptions, TwinsOptions).GetReportAsync();

        Assert.False(report.Teams[0].IsUnavailable);
        Assert.True(report.Teams[1].IsUnavailable);
        Assert.Equal("Baseball", report.Teams[1].Label);
    }

    [Fact]
    public async Task AllTeamsFail_Throws()
    {
        var handler = new FakeHttpHandler().Fail("schedule");

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(handler, VikingsOptions).GetReportAsync());
    }

    [Fact]
    public async Task SecondCall_IsServedFromCache()
    {
        var handler = new FakeHttpHandler()
            .Json($"{VikingsSchedule}?seasontype=2", Schedule(Vikings, 2026, 2, "2026", null,
                Event("2", "2026-10-25T17:00Z", "16", "11", "IND", home: true, "pre", completed: false)));
        var service = CreateService(handler, VikingsOptions);

        await service.GetReportAsync();
        var requestsAfterFirst = handler.Requests.Count;
        await service.GetReportAsync();

        Assert.Equal(requestsAfterFirst, handler.Requests.Count);
    }
}
