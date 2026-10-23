namespace JtsCommandCenter.Core.Projects;

/// <summary>One piece of a project: a card on that project's subtask board.</summary>
public sealed class Subtask
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public string Title { get; set; } = "";

    /// <summary>Markdown.</summary>
    public string? Description { get; set; }

    public SubtaskStatus Status { get; set; }
    public Priority Priority { get; set; } = Priority.Normal;
    public DateOnly? StartDate { get; set; }
    public DateOnly? DueDate { get; set; }

    /// <summary>Set when the subtask moves to Done, cleared if it moves back out (UTC).</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Why the subtask is Blocked. Cleared when it leaves Blocked.</summary>
    public string? BlockedReason { get; set; }

    /// <summary>Position within its board column.</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Changes on every edit; an edit made against an older version is rejected.</summary>
    public Guid Version { get; set; }

    public List<Note> Notes { get; set; } = [];
    public List<Link> Links { get; set; } = [];
    public List<Attachment> Attachments { get; set; } = [];
}
