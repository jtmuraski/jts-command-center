namespace JtsCommandCenter.Web.Components.Shared;

public static class RelativeTime
{
    /// <summary>A short age for a timestamp: "just now", "23 min ago", "3 hr ago", "2d ago", then the date.</summary>
    public static string Ago(DateTimeOffset published, DateTimeOffset now)
    {
        var age = now - published;
        return age.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => $"{(int)age.TotalMinutes} min ago",
            < 60 * 24 => $"{(int)age.TotalHours} hr ago",
            < 60 * 24 * 7 => $"{(int)age.TotalDays}d ago",
            _ => published.ToLocalTime().ToString("MMM d")
        };
    }
}
