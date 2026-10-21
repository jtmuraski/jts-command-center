using JtsCommandCenter.Core.Sports;
using JtsCommandCenter.Infrastructure.Sports;
using JtsCommandCenter.Tests.Support;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JtsCommandCenter.Tests.Sports;

public class EspnGameDetailServiceTests
{
    private const string Summary = "football/nfl/summary";

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static EspnGameDetailService CreateService(FakeHttpHandler handler)
    {
        var options = new SportsCenterOptions();
        options.Leagues["nfl"] = new LeagueOptions { Name = "NFL", Sport = "football", League = "nfl" };

        return new EspnGameDetailService(
            handler.CreateClient(),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(options),
            Options.Create(new SportsOptions { TimeZone = "America/Chicago" }),
            NullLogger<EspnGameDetailService>.Instance);
    }

    private static Task<GameDetail> FinalGame() =>
        CreateService(new FakeHttpHandler().Json($"{Summary}?event=900", Fixture("nfl-summary-final.json"))).GetGameAsync("nfl", "900");

    private static Task<GameDetail> Pregame() =>
        CreateService(new FakeHttpHandler().Json($"{Summary}?event=901", Fixture("nfl-summary-pregame.json"))).GetGameAsync("nfl", "901");

    [Fact]
    public async Task Header_TeamsScoresAndLineScores()
    {
        var game = await FinalGame();

        Assert.Equal(GameState.Final, game.State);
        Assert.Equal("Final/OT", game.StatusDetail);
        Assert.Equal((2, 5), (game.SeasonType, game.Week));
        Assert.Equal(new DateTime(2026, 10, 11, 12, 0, 0), game.Start);

        Assert.Equal(("MIN", 23, true), (game.Away.Abbreviation, game.Away.Score, game.Away.IsWinner));
        Assert.Equal(("NO", 20, false), (game.Home.Abbreviation, game.Home.Score, game.Home.IsWinner));
        Assert.Equal("Vikings", game.Away.ShortName);
        Assert.Equal("5-0", game.Away.Record);
        Assert.Equal([3, 3, 7, 7, 3], game.Away.Linescores);
        Assert.Equal(["1", "2", "3", "4", "OT"], game.PeriodLabels);

        // The dark-background logo variant is preferred.
        Assert.Contains("/500-dark/", game.Home.LogoUrl);
    }

    [Fact]
    public async Task Info_VenueAttendanceOfficialsBroadcast()
    {
        var game = await FinalGame();

        Assert.Equal("Caesars Superdome", game.Info.Venue);
        Assert.Equal("New Orleans, LA", game.Info.Location);
        Assert.Equal(70006, game.Info.Attendance);
        Assert.Equal("Fox", game.Info.Broadcast);
        Assert.Equal(new Official("John Hussey", "Referee"), Assert.Single(game.Info.Officials));
        Assert.Null(game.Info.Weather); // dome game: no weather block
    }

    [Fact]
    public async Task TeamStats_PairAwayWithHomeAndComputeComparableNumbers()
    {
        var game = await FinalGame();
        var stat = (string key) => game.TeamStats.Single(s => s.Key == key);

        Assert.Equal(["firstDowns", "thirdDownEff", "totalPenaltiesYards", "interceptions", "possessionTime"], game.TeamStats.Select(s => s.Key));

        Assert.Equal(("18", "12", 18d, 12d), (stat("firstDowns").AwayValue, stat("firstDowns").HomeValue, stat("firstDowns").AwayNumber, stat("firstDowns").HomeNumber));
        Assert.Equal(0.5, stat("thirdDownEff").AwayNumber);
        Assert.Equal(0.25, stat("thirdDownEff").HomeNumber);
        Assert.Equal((4d, 6d), (stat("totalPenaltiesYards").AwayNumber, stat("totalPenaltiesYards").HomeNumber)); // by count
        Assert.True(stat("totalPenaltiesYards").LowerIsBetter);
        Assert.True(stat("interceptions").LowerIsBetter);
        Assert.False(stat("firstDowns").LowerIsBetter);
        Assert.Equal(39 + 37 / 60.0, stat("possessionTime").AwayNumber!.Value, precision: 6);
    }

    [Fact]
    public async Task PlayerStats_OrderedByCategoryAndSkipEmptyTables()
    {
        var game = await FinalGame();

        Assert.Equal(["passing", "receiving"], game.PlayerStats.Select(t => t.Category));

        var passing = game.PlayerStats[0];
        Assert.Equal("Passing", passing.Title);
        Assert.Equal("MIN", passing.TeamAbbreviation);
        Assert.Equal(["C/ATT", "YDS"], passing.Labels);
        Assert.Equal(new[] { "24/30", "211" }, passing.Totals);
        var murray = Assert.Single(passing.Players);
        Assert.Equal(("Kyler Murray", "1"), (murray.Name, murray.Jersey));
        Assert.Equal(["24/30", "229"], murray.Stats);
    }

    [Fact]
    public async Task ScoringPlays_AndDrivesWithPlays()
    {
        var game = await FinalGame();

        var fieldGoal = Assert.Single(game.ScoringPlays);
        Assert.Equal((1, "9:04", "MIN", "FG", 3, 0), (fieldGoal.Period, fieldGoal.Clock, fieldGoal.TeamAbbreviation, fieldGoal.TypeAbbreviation, fieldGoal.AwayScore, fieldGoal.HomeScore));

        Assert.Equal(2, game.Drives.Count);
        Assert.Equal(5, game.PlayCount);

        var opening = game.Drives[0];
        Assert.Equal(("MIN", "Field Goal", true, "MIN 28", "NO 21", 51), (opening.TeamAbbreviation, opening.Result, opening.IsScore, opening.StartPosition, opening.EndPosition, opening.Yards));

        var kickoff = opening.Plays[0];
        Assert.Null(kickoff.DownDistance); // not from scrimmage
        Assert.Null(kickoff.Yards);

        var rush = opening.Plays[1];
        Assert.Equal(("1st & 10 at MIN 28", 6, "Rush"), (rush.DownDistance, rush.Yards, rush.Type));
        var timeout = opening.Plays[2];
        Assert.Null(timeout.DownDistance); // ESPN tags timeouts with the down, but they aren't snaps
        Assert.Null(timeout.Yards);

        Assert.True(opening.Plays[3].IsScoring);

        var pick = game.Drives[1].Plays[0];
        Assert.True(pick.IsTurnover);
        Assert.Null(pick.Yards); // the 27 is the defense's return, not an offensive gain
        Assert.Equal(2, pick.Period);
        Assert.Null(game.Drives[1].EndPosition);
    }

    [Fact]
    public async Task WinProbability_MapsPlaysToQuartersAndClamps()
    {
        var game = await FinalGame();

        Assert.Equal([1, 1, 1, 2], game.WinProbability.Select(p => p.Period));
        Assert.Equal(1.0, game.WinProbability[^1].HomeWinPercentage); // 1.4 clamped
    }

    [Fact]
    public async Task LeadersAndInjuries_SkipEmptyEntries()
    {
        var game = await FinalGame();

        var leaders = Assert.Single(game.Leaders);
        Assert.Equal(new Leader("Passing Yards", "Kyler Murray", "QB", "24/30, 229 YDS, 1 TD"), Assert.Single(leaders.Leaders));

        var injuries = Assert.Single(game.Injuries);
        Assert.Equal(new Injury("T.J. Hockenson", "TE", "Questionable", "Concussion"), Assert.Single(injuries.Injuries));
    }

    [Fact]
    public async Task FinalGame_HasNoPregame()
    {
        var game = await FinalGame();

        Assert.Null(game.Pregame);
    }

    [Fact]
    public async Task Pregame_PredictorLineFormAndWeather()
    {
        var game = await Pregame();

        Assert.Equal(GameState.Scheduled, game.State);
        Assert.Equal(new DateTime(2026, 10, 25, 12, 0, 0), game.Start);
        Assert.Empty(game.TeamStats);
        Assert.Empty(game.PlayerStats);
        Assert.Empty(game.Drives);
        Assert.Empty(game.PeriodLabels);
        Assert.Null(game.Home.Score);
        Assert.Equal("52° · 51% precip · gusts 12 mph", game.Info.Weather);
        Assert.Contains("/500-dark/", game.Home.LogoUrl); // derived from the plain "logo" field

        var pregame = Assert.IsType<Pregame>(game.Pregame);
        Assert.Equal((65.4, 34.3), (pregame.HomeWinChance, pregame.AwayWinChance));
        Assert.Equal(new BettingLine("DraftKings", "MIN -6.5", 44.5, 240, -300), pregame.Line);
        Assert.Equal(["MIN", "IND"], pregame.RecentForm.Select(f => f.TeamAbbreviation));
        Assert.Equal(new RecentGame("L", "33-30 OT", "@", "KC"), pregame.RecentForm[1].Games[0]);

        Assert.Equal("Rushing Yards", game.Leaders[0].Leaders[0].Category);
    }

    [Fact]
    public async Task SecondRequest_IsServedFromCache()
    {
        var handler = new FakeHttpHandler().Json($"{Summary}?event=900", Fixture("nfl-summary-final.json"));
        var service = CreateService(handler);

        await service.GetGameAsync("nfl", "900");
        await service.GetGameAsync("nfl", "900");

        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("firstDowns", "18", 18d)]
    [InlineData("yardsPerPlay", "5.4", 5.4)]
    [InlineData("thirdDownEff", "9-18", 0.5)]
    [InlineData("completionAttempts", "24/30", 0.8)]
    [InlineData("fourthDownEff", "0-0", 0d)]
    [InlineData("sacksYardsLost", "2-18", 2d)]
    [InlineData("possessionTime", "30:00", 30d)]
    [InlineData("anything", "--", null)]
    public void StatNumber_ParsesEspnDisplayValues(string key, string value, double? expected)
    {
        Assert.Equal(expected, EspnGameDetailService.StatNumber(key, value));
    }
}
