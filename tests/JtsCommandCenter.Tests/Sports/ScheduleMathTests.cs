using JtsCommandCenter.Core.Sports;

namespace JtsCommandCenter.Tests.Sports;

public class ScheduleMathTests
{
    private static readonly Opponent Texas = new("TEX", "Texas Rangers", null);
    private static readonly Opponent Detroit = new("DET", "Detroit Tigers", null);
    private static readonly Opponent Cleveland = new("CLE", "Cleveland Guardians", null);

    // Three 3-game series: TEX at home Sep 10–12, at DET Sep 14–16, CLE at home Sep 17–19.
    private static readonly (int Day, Opponent Opponent, bool Home)[] Slate =
    [
        (10, Texas, true), (11, Texas, true), (12, Texas, true),
        (14, Detroit, false), (15, Detroit, false), (16, Detroit, false),
        (17, Cleveland, true), (18, Cleveland, true), (19, Cleveland, true),
    ];

    /// <summary>Builds the slate with the first <paramref name="played"/> games final (wins on even indexes).</summary>
    private static List<Game> Schedule(int played) =>
        Slate.Select((g, i) =>
        {
            var final = i < played;
            return new Game(
                $"{g.Day}{g.Opponent.Abbreviation}",
                new DateTime(2026, 9, g.Day, 19, 10, 0),
                false,
                final ? GameState.Final : GameState.Scheduled,
                final ? "Final" : "",
                g.Home,
                g.Opponent,
                final ? (i % 2 == 0 ? 5 : 2) : null,
                final ? 3 : null,
                false,
                null);
        }).ToList();

    private static (Series? Current, Series? Next) Pick(int played, int today) =>
        ScheduleMath.CurrentAndNextSeries(ScheduleMath.GroupSeries(Schedule(played)), new DateOnly(2026, 9, today));

    [Fact]
    public void GroupSeries_SplitsOnOpponentAndPark()
    {
        var series = ScheduleMath.GroupSeries(Schedule(0));

        Assert.Equal(["TEX", "DET", "CLE"], series.Select(s => s.Opponent.Abbreviation));
        Assert.All(series, s => Assert.Equal(3, s.Games.Count));
        Assert.Equal(new DateOnly(2026, 9, 14), series[1].FirstDay);
        Assert.Equal(new DateOnly(2026, 9, 16), series[1].LastDay);
    }

    [Fact]
    public void GroupSeries_SameOpponentAtTheOtherPark_IsANewSeries()
    {
        var games = Schedule(0);
        games[3] = games[3] with { Opponent = Texas, IsHome = false };

        var series = ScheduleMath.GroupSeries(games);

        Assert.Equal(4, series.Count);
        Assert.Equal(3, series[0].Games.Count);
        Assert.False(series[1].IsHome);
    }

    [Fact]
    public void MidSeries_CurrentIsInProgressSeries()
    {
        var (current, next) = Pick(played: 2, today: 12);

        Assert.Equal("TEX", current?.Opponent.Abbreviation);
        Assert.False(current!.IsComplete);
        Assert.Equal((1, 1), (current.Wins, current.Losses));
        Assert.Equal("DET", next?.Opponent.Abbreviation);
    }

    [Fact]
    public void OffDayBetweenSeries_FinishedSeriesStaysCurrent()
    {
        var (current, next) = Pick(played: 3, today: 13);

        Assert.Equal("TEX", current?.Opponent.Abbreviation);
        Assert.True(current!.IsComplete);
        Assert.Equal((2, 1), (current.Wins, current.Losses));
        Assert.Equal("DET", next?.Opponent.Abbreviation);
    }

    [Fact]
    public void FirstGameDayOfNewSeries_NewSeriesBecomesCurrent()
    {
        var (current, next) = Pick(played: 3, today: 14);

        Assert.Equal("DET", current?.Opponent.Abbreviation);
        Assert.Equal("CLE", next?.Opponent.Abbreviation);
    }

    [Fact]
    public void LastSeriesOfSeason_HasNoNextSeries()
    {
        var (current, next) = Pick(played: 6, today: 17);

        Assert.Equal("CLE", current?.Opponent.Abbreviation);
        Assert.Null(next);
    }

    [Fact]
    public void BeforeOpeningDay_FirstSeriesIsCurrent()
    {
        var (current, next) = Pick(played: 0, today: 9);

        Assert.Equal("TEX", current?.Opponent.Abbreviation);
        Assert.Equal("DET", next?.Opponent.Abbreviation);
    }

    [Fact]
    public void AllGamesPlayed_LastSeriesIsCurrent()
    {
        var (current, next) = Pick(played: 9, today: 25);

        Assert.Equal("CLE", current?.Opponent.Abbreviation);
        Assert.True(current!.IsComplete);
        Assert.Null(next);
    }

    [Fact]
    public void NextGame_PrefersLiveOverScheduled()
    {
        var games = Schedule(2);
        games[2] = games[2] with { State = GameState.Live };

        Assert.Equal(games[2], ScheduleMath.NextGame(games));
        Assert.Equal(games[1], ScheduleMath.LastGame(games));
    }

    [Theory]
    [InlineData(5, 3, GameResult.Win)]
    [InlineData(2, 3, GameResult.Loss)]
    [InlineData(3, 3, GameResult.Tie)]
    public void Result_ComparesScores(int ours, int theirs, GameResult expected)
    {
        var game = Schedule(1)[0] with { Score = ours, OpponentScore = theirs };

        Assert.Equal(expected, game.Result);
    }

    [Fact]
    public void Result_IsNullUntilFinal()
    {
        var game = Schedule(1)[0] with { State = GameState.Live };

        Assert.Null(game.Result);
    }
}
