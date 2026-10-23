namespace JtsCommandCenter.Web.Components.Projects;

/// <summary>Short date labels for cards: "Oct 30", with the year only when it isn't the current one.</summary>
public static class DateText
{
    public static string Short(DateOnly date, DateOnly today) =>
        date.ToString(date.Year == today.Year ? "MMM d" : "MMM d, yyyy");
}
