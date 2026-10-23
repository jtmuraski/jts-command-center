namespace JtsCommandCenter.Core.Projects;

/// <summary>Settings bound from the "Projects" section of appsettings.json.</summary>
public sealed class ProjectsOptions
{
    public const string SectionName = "Projects";

    /// <summary>Decides what "today" is for due dates, since the server may run in UTC.</summary>
    public string TimeZone { get; set; } = "America/Chicago";
}
