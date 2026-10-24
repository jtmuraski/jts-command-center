using JtsCommandCenter.Core.Sports;

namespace JtsCommandCenter.Tests.Sports;

public class GamePeriodsTests
{
    [Theory]
    [InlineData("football", 2, 4, "4", "Q4", "4th quarter")]
    [InlineData("football", 2, 5, "OT", "OT", "Overtime")]
    [InlineData("football", 3, 6, "OT2", "OT2", "Overtime 2")]
    [InlineData("baseball", 2, 1, "1", "1st", "1st inning")]
    [InlineData("baseball", 3, 12, "12", "12th", "12th inning")]
    [InlineData("hockey", 2, 3, "3", "3rd", "3rd period")]
    [InlineData("hockey", 2, 4, "OT", "OT", "Overtime")]
    [InlineData("hockey", 2, 5, "SO", "SO", "Shootout")]
    [InlineData("hockey", 3, 5, "2OT", "2OT", "2nd overtime")]
    [InlineData("hockey", 3, 6, "3OT", "3OT", "3rd overtime")]
    public void NamesPeriodsPerSport(string sport, int seasonType, int period, string column, string shortName, string name)
    {
        Assert.Equal(column, GamePeriods.Column(sport, seasonType, period));
        Assert.Equal(shortName, GamePeriods.Short(sport, seasonType, period));
        Assert.Equal(name, GamePeriods.Name(sport, seasonType, period));
    }

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(22, "22nd")]
    public void Ordinal(int number, string expected)
    {
        Assert.Equal(expected, GamePeriods.Ordinal(number));
    }

    [Fact]
    public void BaseballPlays_KnowsHits()
    {
        Assert.True(BaseballPlays.IsHit("Single"));
        Assert.True(BaseballPlays.IsHit("home run"));
        Assert.False(BaseballPlays.IsHit("Walk"));
        Assert.False(BaseballPlays.IsHit("Fielders Choice, Runner Out"));
        Assert.False(BaseballPlays.IsHit(BaseballPlays.PitchingChange));
    }

    [Fact]
    public void WeekRef_DayPagesCompareByDate()
    {
        var day = WeekRef.ForDay(new DateOnly(2026, 10, 23));

        Assert.True(day.IsDay);
        Assert.Equal("Friday", day.Label);
        Assert.True(day.SamePage(WeekRef.ForDay(new DateOnly(2026, 10, 23))));
        Assert.False(day.SamePage(WeekRef.ForDay(new DateOnly(2026, 10, 24))));

        var week = new WeekRef(2, 7, "Week 7", default, default);
        Assert.False(week.IsDay);
        Assert.True(week.SamePage(new WeekRef(2, 7, "", new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 27))));
        Assert.False(week.SamePage(day));
    }
}
