using JtsCommandCenter.Core.Sports;
using JtsCommandCenter.Infrastructure.Sports;
using JtsCommandCenter.Tests.Support;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JtsCommandCenter.Tests.Sports;

/// <summary>
/// <see cref="EspnGameDetailService"/> on NHL games. The fixtures are trimmed ESPN responses: CAR at EDM,
/// won by Edmonton in a shootout (goals, penalties and a few other events), and MTL at CHI before puck drop.
/// </summary>
public class EspnHockeyGameTests
{
    private const string Summary = "hockey/nhl/summary";

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static Task<GameDetail> Load(string eventId, string fixture)
    {
        var options = new SportsCenterOptions();
        options.Leagues["nhl"] = new LeagueOptions { Name = "NHL", Sport = "hockey", League = "nhl" };

        var service = new EspnGameDetailService(
            new FakeHttpHandler().Json($"{Summary}?event={eventId}", Fixture(fixture)).CreateClient(),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(options),
            Options.Create(new SportsOptions { TimeZone = "America/Chicago" }),
            NullLogger<EspnGameDetailService>.Instance);
        return service.GetGameAsync("nhl", eventId);
    }

    private static Task<GameDetail> ShootoutGame() => Load("401892561", "nhl-summary-final.json");

    private static Task<GameDetail> Pregame() => Load("401892564", "nhl-summary-pregame.json");

    [Fact]
    public async Task Header_PeriodsThroughShootout()
    {
        var game = await ShootoutGame();

        Assert.Equal(("hockey", "Final/SO", 2), (game.Sport, game.StatusDetail, game.SeasonType));
        Assert.Equal(("CAR", 5, false), (game.Away.Abbreviation, game.Away.Score, game.Away.IsWinner));
        Assert.Equal(("EDM", 6, true), (game.Home.Abbreviation, game.Home.Score, game.Home.IsWinner));
        Assert.Equal(["1", "2", "3", "OT", "SO"], game.PeriodLabels);
        Assert.Equal([1, 1, 3, 0, 1], game.Home.Linescores);
        Assert.Null(game.Home.Hits);
        Assert.Equal("EDM leads series 1-0", game.Note);
        Assert.Empty(game.WinProbability); // ESPN has none for hockey
    }

    [Fact]
    public async Task PlayByPlay_GroupedByPeriodWithGoalsAndShots()
    {
        var game = await ShootoutGame();

        Assert.Equal(["1st period", "2nd period", "3rd period", "Overtime", "Shootout"], game.Drives.Select(d => d.Result));
        Assert.All(game.Drives, d => Assert.Equal("", d.TeamAbbreviation));

        var first = game.Drives[0];
        Assert.Equal("2 goals · shots CAR 2, EDM 1", first.Summary);
        Assert.True(first.IsScore);
        Assert.False(game.Drives[3].IsScore);

        // The shootout keeps its own tally.
        Assert.Equal("CAR 0, EDM 1", game.Drives[4].Summary);

        var penalty = first.Plays.Single(p => p.Type == "Tripping");
        Assert.True(penalty.IsPenalty);
        Assert.Equal("Nicolas Deslauriers Tripping against Colton Dach (2 min)", penalty.Text);
        Assert.Equal((1, "8:04"), (penalty.Period, penalty.Clock));

        var powerPlayGoal = first.Plays.First(p => p.IsScoring);
        Assert.Equal(("Goal", "Power Play"), (powerPlayGoal.Type, powerPlayGoal.DownDistance));
        Assert.Null(game.Drives[1].Plays.First(p => p.IsScoring).DownDistance); // even strength: no tag
        Assert.All(game.Drives.SelectMany(d => d.Plays), p => Assert.Null(p.Yards));
    }

    [Fact]
    public async Task ScoringPlays_AreTheGoalsWithStrengthBadges()
    {
        var game = await ShootoutGame();

        Assert.Equal(11, game.ScoringPlays.Count);

        var first = game.ScoringPlays[0];
        Assert.Equal((1, "8:07", "EDM", "Power Play", "PPG", 0, 1), (first.Period, first.Clock, first.TeamAbbreviation, first.Type, first.TypeAbbreviation, first.AwayScore, first.HomeScore));
        Assert.Contains("/500-dark/", first.TeamLogoUrl);

        Assert.Equal("G", game.ScoringPlays[2].TypeAbbreviation);
        Assert.Equal((5, "SO", "EDM"), (game.ScoringPlays[^1].Period, game.ScoringPlays[^1].TypeAbbreviation, game.ScoringPlays[^1].TeamAbbreviation));
    }

    [Fact]
    public async Task BoxScore_KeepsTheUsefulColumnsInOrder()
    {
        var game = await ShootoutGame();

        // The empty "skaters" table is skipped.
        Assert.Equal(["forwards", "forwards", "defenses", "defenses", "goalies", "goalies"], game.PlayerStats.Select(t => t.Category));

        var forwards = game.PlayerStats[0];
        Assert.Equal(("CAR", "Forwards"), (forwards.TeamAbbreviation, forwards.Title));
        Assert.Equal(["G", "A", "+/-", "S", "HT", "BS", "PIM", "TOI", "SHFT", "FO%"], forwards.Labels);
        Assert.Equal(("Sebastian Aho", "20"), (forwards.Players[0].Name, forwards.Players[0].Jersey));
        Assert.Equal(["1", "1", "0", "3", "4", "0", "0", "22:06", "26", "63.2"], forwards.Players[0].Stats);

        var goalies = game.PlayerStats[4];
        Assert.Equal(["SA", "GA", "SV", "SV%", "ESSV", "PPSV", "SHSV", "TOI"], goalies.Labels);
        Assert.Equal(["21", "5", "16", ".762", "11", "4", "1", "65:00"], goalies.Players[0].Stats);
    }

    [Fact]
    public async Task TeamStats_FlatListWithHockeyLowerIsBetter()
    {
        var game = await ShootoutGame();
        var stat = (string key) => game.TeamStats.Single(s => s.Key == key);

        Assert.Equal(["shotsTotal", "powerPlayGoals", "faceoffPercent", "giveaways", "penaltyMinutes"], game.TeamStats.Select(s => s.Key));
        Assert.Equal("27", stat("shotsTotal").AwayValue);
        Assert.True(stat("giveaways").LowerIsBetter);
        Assert.True(stat("penaltyMinutes").LowerIsBetter);
        Assert.False(stat("shotsTotal").LowerIsBetter);
    }

    [Fact]
    public async Task Pregame_ProbableGoaliesAndLine()
    {
        var game = await Pregame();

        Assert.Equal(GameState.Scheduled, game.State);
        Assert.Equal(new DateTime(2026, 10, 23, 19, 0, 0), game.Start);

        var pregame = Assert.IsType<Pregame>(game.Pregame);
        Assert.Equal(
            [new ProbableStarter("MTL", "Jakub Dobes", "1-2-2, 4.47 GAA, .847 SV%"), new ProbableStarter("CHI", "Spencer Knight", "3-4-0, 3.59 GAA, .888 SV%")],
            pregame.Starters);
        Assert.Null(pregame.HomeWinChance); // no predictor for hockey
        Assert.Equal(new BettingLine("DraftKings", "MTL -170", 6.5, -170, 142), pregame.Line);

        Assert.True(game.TeamStats.Single(s => s.Key == "avgGoalsAgainst").LowerIsBetter);
        Assert.Equal(new Leader("Goals", "Frank Nazar", "C", "3"), game.Leaders[0].Leaders[0]);
    }
}
