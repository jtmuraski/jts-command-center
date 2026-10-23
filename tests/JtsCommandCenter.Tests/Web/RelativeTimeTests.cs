using JtsCommandCenter.Web.Components.Shared;

namespace JtsCommandCenter.Tests.Web;

public class RelativeTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0.5, "just now")]
    [InlineData(23, "23 min ago")]
    [InlineData(185, "3 hr ago")]
    [InlineData(60 * 24 * 2 + 5, "2d ago")]
    public void Ago_ShortensRecentTimes(double minutesAgo, string expected) =>
        Assert.Equal(expected, RelativeTime.Ago(Now.AddMinutes(-minutesAgo), Now));

    [Fact]
    public void Ago_ShowsTheDateAfterAWeek() =>
        Assert.Equal("Oct 1", RelativeTime.Ago(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), Now));
}
