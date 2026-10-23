namespace JtsCommandCenter.Core.Projects;

/// <summary>The editable fields of a project. Status and position change through <see cref="IProjectService.MoveProjectAsync"/>.</summary>
public sealed record ProjectInput(
    string Title,
    string? Description,
    WorkArea Area,
    Priority Priority = Priority.Normal,
    DateOnly? StartDate = null,
    DateOnly? DueDate = null);

/// <summary>The editable fields of a subtask. Status and position change through <see cref="IProjectService.MoveSubtaskAsync"/>.</summary>
public sealed record SubtaskInput(
    string Title,
    string? Description,
    Priority Priority = Priority.Normal,
    DateOnly? StartDate = null,
    DateOnly? DueDate = null);

/// <summary>
/// Projects, their subtasks, work-log notes, and links. Results are detached snapshots; change them
/// through these methods. Missing ids throw <see cref="KeyNotFoundException"/>, invalid input throws
/// <see cref="ArgumentException"/>, and an edit against an outdated version throws
/// <see cref="ConcurrencyConflictException"/>.
/// </summary>
public interface IProjectService
{
    /// <summary>Today's date in the configured time zone, for due dates and overdue checks.</summary>
    DateOnly Today { get; }

    /// <summary>A stored UTC timestamp (created, completed, note times) in the configured time zone.</summary>
    DateTime ToLocalTime(DateTime utc);

    /// <summary>Projects for the board (or the archive), each with its subtasks for progress, in column order.</summary>
    Task<IReadOnlyList<Project>> GetProjectsAsync(bool archived = false, CancellationToken cancellationToken = default);

    /// <summary>A project with its subtasks (column order), links, and notes (newest first), or null.</summary>
    Task<Project?> GetProjectAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Adds a project to the bottom of a board column.</summary>
    Task<Project> CreateProjectAsync(ProjectInput input, ProjectStatus status = ProjectStatus.Planned, CancellationToken cancellationToken = default);

    /// <summary>Saves edits made against <paramref name="version"/>.</summary>
    Task<Project> UpdateProjectAsync(int id, Guid version, ProjectInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a project to <paramref name="index"/> in a board column. A status change is logged, sets or
    /// clears the completed date, and keeps <paramref name="reason"/> while On Hold. Moves don't bump the
    /// version, since they never overwrite edited fields.
    /// </summary>
    Task MoveProjectAsync(int id, ProjectStatus status, int index, string? reason = null, CancellationToken cancellationToken = default);

    Task ArchiveProjectAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Puts an archived project back on the board, at the bottom of its column.</summary>
    Task RestoreProjectAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes an archived project with everything in it. Active projects must be archived first.</summary>
    Task DeleteProjectAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>A subtask with its project, links, and notes (newest first), or null.</summary>
    Task<Subtask?> GetSubtaskAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Adds a subtask to the bottom of a column on its project's board.</summary>
    Task<Subtask> CreateSubtaskAsync(int projectId, SubtaskInput input, SubtaskStatus status = SubtaskStatus.ToDo, CancellationToken cancellationToken = default);

    /// <summary>Saves edits made against <paramref name="version"/>.</summary>
    Task<Subtask> UpdateSubtaskAsync(int id, Guid version, SubtaskInput input, CancellationToken cancellationToken = default);

    /// <summary>Moves a subtask within its project's board. Same rules as <see cref="MoveProjectAsync"/>, with Blocked keeping the reason.</summary>
    Task MoveSubtaskAsync(int id, SubtaskStatus status, int index, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes a subtask with its notes and links, and logs it on the project.</summary>
    Task DeleteSubtaskAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Unfinished subtasks of active projects that are due today or overdue, oldest due date first.</summary>
    Task<IReadOnlyList<Subtask>> GetDueSubtasksAsync(CancellationToken cancellationToken = default);

    Task<Note> AddProjectNoteAsync(int projectId, string body, CancellationToken cancellationToken = default);

    Task<Note> AddSubtaskNoteAsync(int subtaskId, string body, CancellationToken cancellationToken = default);

    /// <summary>Edits a hand-written entry. Activity notes can't be edited.</summary>
    Task<Note> UpdateNoteAsync(int id, string body, CancellationToken cancellationToken = default);

    Task DeleteNoteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Adds a link. A blank label falls back to the URL's host.</summary>
    Task<Link> AddProjectLinkAsync(int projectId, string? label, string url, CancellationToken cancellationToken = default);

    Task<Link> AddSubtaskLinkAsync(int subtaskId, string? label, string url, CancellationToken cancellationToken = default);

    Task DeleteLinkAsync(int id, CancellationToken cancellationToken = default);
}
