using JtsCommandCenter.Core.Sports;
using JtsCommandCenter.Infrastructure.Sports;
using JtsCommandCenter.Tests.Support;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JtsCommandCenter.Tests.Sports;

/// <summary>
/// <see cref="EspnGameDetailService"/> on MLB games. The fixtures are trimmed ESPN responses: ALCS game 6
/// (the first two innings of plays) and World Series game 1 before first pitch.
/// </summary>
public class EspnBaseballGameTests
{
    private const string Summary = "baseball/mlb/summary";

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static Task<GameDetail> Load(string eventId, string fixture)
    {
        var options = new SportsCenterOptions();
        options.Leagues["mlb"] = new LeagueOptions { Name = "MLB", Sport = "baseball", League = "mlb" };

        var service = new EspnGameDetailService(
            new FakeHttpHandler().Json($"{Summary}?event={eventId}", Fixture(fixture)).CreateClient(),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(options),
            Options.Create(new SportsOptions { TimeZone = "America/Chicago" }),
            NullLogger<EspnGameDetailService>.Instance);
        return service.GetGameAsync("mlb", eventId);
    }

    private static Task<GameDetail> FinalGame() => Load("401908028", "mlb-summary-final.json");

    private static Task<GameDetail> Pregame() => Load("401908042", "mlb-summary-pregame.json");

    [Fact]
    public async Task Header_LineScoreWithHitsErrorsAndSeriesNote()
    {
        var game = await FinalGame();

        Assert.Equal(("baseball", GameState.Final, "Final", 3), (game.Sport, game.State, game.StatusDetail, game.SeasonType));
        Assert.Null(game.Week);
        Assert.Equal("ALCS - Game 6 · Series tied 3-3", game.Note);

        Assert.Equal(("CLE", 3, 7, 0), (game.Away.Abbreviation, game.Away.Score, game.Away.Hits, game.Away.Errors));
        Assert.Equal(("TB", 7, true), (game.Home.Abbreviation, game.Home.Score, game.Home.IsWinner));
        Assert.Equal(9, game.Away.Linescores.Count);
        Assert.Equal(8, game.Home.Linescores.Count); // the home team didn't need the bottom of the 9th
        Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8", "9"], game.PeriodLabels);
        Assert.Equal("7th inning", game.PeriodName(7));
    }

    [Fact]
    public async Task PlayByPlay_IsAtBatResultsGroupedByHalfInning()
    {
        var game = await FinalGame();

        Assert.Equal(["Top 1st", "Bottom 1st", "Top 2nd", "Bottom 2nd"], game.Drives.Select(d => d.Result));

        var top1 = game.Drives[0];
        Assert.Equal(("CLE", "0 runs · 1 hit", false), (top1.TeamAbbreviation, top1.Summary, top1.IsScore));
        Assert.Equal("Kwan reached on infield single to shortstop.", top1.Plays[0].Text);
        Assert.Equal(("Single", 0), (top1.Plays[0].Type, top1.Plays[0].Outs));
        Assert.Equal(3, top1.Plays[^1].Outs);

        var bottom2 = game.Drives[3];
        Assert.Equal(("TB", "2 runs · 2 hits", true), (bottom2.TeamAbbreviation, bottom2.Summary, bottom2.IsScore));
        Assert.Equal(
            ["Single", "Pitching Change", "Fielders Choice, Runner Out", "Stolen Base", "Walk", "Line Out", "Stolen Base", "Double", "Pitching Change", "Walk", "Hit by Pitch", "Fly Out"],
            bottom2.Plays.Select(p => p.Type));

        // ESPN reports 0 outs on a pitching change, so none is shown.
        Assert.Null(bottom2.Plays[1].Outs);
        Assert.Equal("Herrin relieved Armstrong", bottom2.Plays[1].Text);
        Assert.All(game.Drives.SelectMany(d => d.Plays), p => Assert.Equal("", p.Clock));
    }

    [Fact]
    public async Task ScoringPlays_ComeFromThePlays()
    {
        var game = await FinalGame();

        var play = Assert.Single(game.ScoringPlays);
        Assert.Equal((2, "Bottom", "TB", "Double", "2B", 0, 2), (play.Period, play.Clock, play.TeamAbbreviation, play.Type, play.TypeAbbreviation, play.AwayScore, play.HomeScore));
        Assert.StartsWith("Palacios doubled to right", play.Text);
    }

    [Fact]
    public async Task WinProbability_FollowsTheAtBats()
    {
        var game = await FinalGame();

        Assert.Equal(18, game.WinProbability.Count);
        Assert.Equal(1, game.WinProbability[0].Period);
        Assert.Equal(2, game.WinProbability[^1].Period);
    }

    [Fact]
    public async Task BoxScore_BattingThenPitchingWithPositionsAndDecisions()
    {
        var game = await FinalGame();

        Assert.Equal(["batting", "batting", "pitching", "pitching"], game.PlayerStats.Select(t => t.Category));

        var batting = game.PlayerStats[0];
        Assert.Equal(("CLE", "Batting"), (batting.TeamAbbreviation, batting.Title));
        Assert.Equal(["H-AB", "AB", "R", "H"], batting.Labels.Take(4));
        Assert.Equal(("Steven Kwan", "CF"), (batting.Players[0].Name, batting.Players[0].Detail));

        var pitching = game.PlayerStats.Single(t => t.Category == "pitching" && t.TeamAbbreviation == "TB");
        Assert.Equal(["W, 2-0", "H, 4"], pitching.Players.Select(p => p.Detail));
        Assert.Equal("5.1", pitching.Players[0].Stats[0]);
    }

    [Fact]
    public async Task TeamStats_GameTotalsFromTheGroupedBoxScore()
    {
        var game = await FinalGame();
        var stat = (string key) => game.TeamStats.Single(s => s.Key == key);

        Assert.Equal(
            ["batting.hits", "batting.homeRuns", "batting.RBIs", "batting.walks", "batting.strikeouts", "fielding.errors"],
            game.TeamStats.Select(s => s.Key));
        Assert.Equal(("3", "7", 3d, 7d), (stat("batting.RBIs").AwayValue, stat("batting.RBIs").HomeValue, stat("batting.RBIs").AwayNumber, stat("batting.RBIs").HomeNumber));
        Assert.Equal("Struck out", stat("batting.strikeouts").Label);
        Assert.True(stat("batting.strikeouts").LowerIsBetter);
        Assert.True(stat("fielding.errors").LowerIsBetter);
        Assert.False(stat("batting.hits").LowerIsBetter);
    }

    [Fact]
    public async Task Info_UmpiresAndVenue()
    {
        var game = await FinalGame();

        Assert.Equal(("Tropicana Field", "St. Petersburg, Florida", 40230), (game.Info.Venue, game.Info.Location, game.Info.Attendance));
        Assert.Equal(new Official("Chad Fairchild", "Home Plate Umpire"), game.Info.Officials[0]);
        Assert.Null(game.Pregame);
    }

    [Fact]
    public async Task Pregame_ProbablePitchersPredictorLineAndSeasonStats()
    {
        var game = await Pregame();

        Assert.Equal(GameState.Scheduled, game.State);
        Assert.Equal(new DateTime(2026, 10, 23, 19, 0, 0), game.Start);
        Assert.Empty(game.PeriodLabels);
        Assert.Empty(game.Drives);
        Assert.Empty(game.ScoringPlays);

        var pregame = Assert.IsType<Pregame>(game.Pregame);
        Assert.Equal(
            [new ProbableStarter("TB", "Nick Martinez", "1-0, 3.00 ERA"), new ProbableStarter("MIL", "Jacob Misiorowski", "0-0, 3.24 ERA")],
            pregame.Starters);
        Assert.Equal((72.7, 27.3), (pregame.HomeWinChance, pregame.AwayWinChance));
        Assert.Equal(new BettingLine("DraftKings", "MIL -194", 6.5, 159, -194), pregame.Line);
        Assert.Equal(new RecentGame("W", "3-1", "@", "SD"), pregame.RecentForm[0].Games[0]);

        // Season figures before first pitch.
        Assert.Equal(["batting.avg", "batting.homeRuns", "pitching.ERA", "fielding.errors"], game.TeamStats.Select(s => s.Key));
        Assert.Equal((3.70, 3.51), (game.TeamStats[2].AwayNumber, game.TeamStats[2].HomeNumber));
        Assert.True(game.TeamStats[2].LowerIsBetter);

        Assert.Equal(new Leader("Batting Average", "Jackson Chourio", "LF", ".291"), game.Leaders[0].Leaders[0]);
    }
}
