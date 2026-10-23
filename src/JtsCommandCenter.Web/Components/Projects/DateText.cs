namespace JtsCommandCenter.Web.Components.Projects;

/// <summary>Short date labels: "Oct 30", with the year only when it isn't the current one.</summary>
public static class DateText
{
    public static string Short(DateOnly date, DateOnly today) =>
        date.ToString(date.Year == today.Year ? "MMM d" : "MMM d, yyyy");

    /// <summary>A local timestamp: just the time today, otherwise the date and time.</summary>
    public static string Stamp(DateTime local, DateOnly today)
    {
        var date = DateOnly.FromDateTime(local);
        return date == today ? local.ToString("h:mm tt") : $"{Short(date, today)}, {local:h:mm tt}";
    }
}
