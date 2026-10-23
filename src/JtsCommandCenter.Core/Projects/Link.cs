namespace JtsCommandCenter.Core.Projects;

/// <summary>A labeled URL (ticket, spec, repo) on a project, or on one of its subtasks when <see cref="SubtaskId"/> is set.</summary>
public sealed class Link
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int? SubtaskId { get; set; }
    public string Label { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
