namespace JtsCommandCenter.Core.Projects;

/// <summary>A work-log entry on a project, or on one of its subtasks when <see cref="SubtaskId"/> is set.</summary>
public sealed class Note
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int? SubtaskId { get; set; }
    public NoteKind Kind { get; set; }

    /// <summary>Markdown.</summary>
    public string Body { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    /// <summary>Set when an entry is edited after it was written (UTC).</summary>
    public DateTime? EditedAt { get; set; }
}
