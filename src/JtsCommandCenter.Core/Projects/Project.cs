namespace JtsCommandCenter.Core.Projects;

/// <summary>A parent project: one card on the projects board, holding its own board of subtasks.</summary>
public sealed class Project
{
    public int Id { get; set; }
    public string Title { get; set; } = "";

    /// <summary>Markdown.</summary>
    public string? Description { get; set; }

    public WorkArea Area { get; set; }
    public ProjectStatus Status { get; set; }
    public Priority Priority { get; set; } = Priority.Normal;
    public DateOnly? StartDate { get; set; }
    public DateOnly? DueDate { get; set; }

    /// <summary>Set when the project moves to Done, cleared if it moves back out (UTC).</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Why the project is On Hold. Cleared when it leaves On Hold.</summary>
    public string? BlockedReason { get; set; }

    /// <summary>Position within its board column.</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Archived projects leave the board but keep their history (UTC).</summary>
    public DateTime? ArchivedAt { get; set; }

    /// <summary>Changes on every edit; an edit made against an older version is rejected.</summary>
    public Guid Version { get; set; }

    public List<Subtask> Subtasks { get; set; } = [];

    /// <summary>The project's work log, including notes on its subtasks.</summary>
    public List<Note> Notes { get; set; } = [];

    /// <summary>Links on the project and on its subtasks.</summary>
    public List<Link> Links { get; set; } = [];
}
