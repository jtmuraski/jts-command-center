namespace JtsCommandCenter.Core.Projects;

/// <summary>Which side of life a project belongs to. Subtasks take their project's area.</summary>
public enum WorkArea
{
    Work,
    Personal,
}

/// <summary>Columns on the projects board, in board order.</summary>
public enum ProjectStatus
{
    Planned,
    Active,
    OnHold,
    Done,
}

/// <summary>Columns on a project's subtask board, in board order.</summary>
public enum SubtaskStatus
{
    ToDo,
    InProgress,
    Blocked,
    Done,
}

public enum Priority
{
    Low,
    Normal,
    High,
}

public enum NoteKind
{
    /// <summary>A work-log entry written by hand.</summary>
    Entry,

    /// <summary>Recorded automatically, e.g. a status change. Can be deleted but not edited.</summary>
    Activity,
}
