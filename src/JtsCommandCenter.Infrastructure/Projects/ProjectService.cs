using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using JtsCommandCenter.Core.Projects;
using JtsCommandCenter.Infrastructure.Data;

namespace JtsCommandCenter.Infrastructure.Projects;

/// <summary>Projects stored with EF Core. Each call uses its own short-lived context.</summary>
internal sealed class ProjectService(
    IDbContextFactory<CommandCenterDbContext> contextFactory,
    TimeProvider time,
    IOptions<ProjectsOptions> options) : IProjectService
{
    private readonly TimeZoneInfo zone = ResolveZone(options.Value.TimeZone);

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime);

    public async Task<IReadOnlyList<Project>> GetProjectsAsync(bool archived = false, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Projects.AsNoTracking()
            .Where(p => (p.ArchivedAt != null) == archived)
            .Include(p => p.Subtasks)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<Project?> GetProjectAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Projects.AsNoTracking()
            .Include(p => p.Subtasks.OrderBy(s => s.SortOrder).ThenBy(s => s.Id))
            .Include(p => p.Notes.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id))
            .Include(p => p.Links.OrderBy(l => l.Id))
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<Project> CreateProjectAsync(ProjectInput input, ProjectStatus status = ProjectStatus.Planned, CancellationToken cancellationToken = default)
    {
        ThrowIfInvalid(input.Title, input.StartDate, input.DueDate);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = Now;
        var project = new Project
        {
            Status = status,
            SortOrder = await NextProjectSortOrderAsync(db, status, cancellationToken),
            CompletedAt = status == ProjectStatus.Done ? now : null,
            CreatedAt = now,
        };
        Apply(project, input, now);

        db.Projects.Add(project);
        await db.SaveChangesAsync(cancellationToken);
        return project;
    }

    public async Task<Project> UpdateProjectAsync(int id, Guid version, ProjectInput input, CancellationToken cancellationToken = default)
    {
        ThrowIfInvalid(input.Title, input.StartDate, input.DueDate);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var project = await FindProjectAsync(db, id, cancellationToken);
        ExpectVersion(db, project, project.Version, version, "project");

        Apply(project, input, Now);
        await SaveEditAsync(db, "project", cancellationToken);
        return project;
    }

    public async Task MoveProjectAsync(int id, ProjectStatus status, int index, string? reason = null, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var project = await FindProjectAsync(db, id, cancellationToken);

        var column = await db.Projects
            .Where(p => p.Status == status && p.ArchivedAt == null && p.Id != id)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);
        Renumber(ProjectMath.Reorder(column, project, index), (p, order) => p.SortOrder = order);

        var now = Now;
        if (project.Status != status)
        {
            var from = project.Status;
            project.Status = status;
            project.CompletedAt = status == ProjectStatus.Done ? now : null;
            project.BlockedReason = status == ProjectStatus.OnHold ? Reason(reason) : null;
            project.UpdatedAt = now;
            LogActivity(db, project.Id, null, ProjectMath.StatusChangeText(
                ProjectMath.DisplayName(from), ProjectMath.DisplayName(status), project.BlockedReason), now);
        }
        else if (status == ProjectStatus.OnHold && Reason(reason) is { } newReason)
        {
            project.BlockedReason = newReason;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ArchiveProjectAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var project = await FindProjectAsync(db, id, cancellationToken);
        if (project.ArchivedAt is not null)
        {
            return;
        }

        var now = Now;
        project.ArchivedAt = now;
        LogActivity(db, project.Id, null, "Archived", now);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RestoreProjectAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var project = await FindProjectAsync(db, id, cancellationToken);
        if (project.ArchivedAt is null)
        {
            return;
        }

        var now = Now;
        project.ArchivedAt = null;
        project.SortOrder = await NextProjectSortOrderAsync(db, project.Status, cancellationToken);
        LogActivity(db, project.Id, null, "Restored from the archive", now);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteProjectAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Load everything so EF deletes notes and links before the subtasks they hang off.
        var project = await db.Projects
            .Include(p => p.Subtasks)
            .Include(p => p.Notes)
            .Include(p => p.Links)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw NotFound("Project", id);

        if (project.ArchivedAt is null)
        {
            throw new InvalidOperationException("Archive the project before deleting it.");
        }

        db.Projects.Remove(project);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Subtask?> GetSubtaskAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Subtasks.AsNoTracking()
            .Include(s => s.Project)
            .Include(s => s.Notes.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id))
            .Include(s => s.Links.OrderBy(l => l.Id))
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<Subtask> CreateSubtaskAsync(int projectId, SubtaskInput input, SubtaskStatus status = SubtaskStatus.ToDo, CancellationToken cancellationToken = default)
    {
        ThrowIfInvalid(input.Title, input.StartDate, input.DueDate);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            throw NotFound("Project", projectId);
        }

        var now = Now;
        var last = await db.Subtasks
            .Where(s => s.ProjectId == projectId && s.Status == status)
            .MaxAsync(s => (int?)s.SortOrder, cancellationToken);
        var subtask = new Subtask
        {
            ProjectId = projectId,
            Status = status,
            SortOrder = (last ?? -1) + 1,
            CompletedAt = status == SubtaskStatus.Done ? now : null,
            CreatedAt = now,
        };
        Apply(subtask, input, now);

        db.Subtasks.Add(subtask);
        LogActivity(db, projectId, null, $"Added subtask \"{subtask.Title}\"", now);
        await db.SaveChangesAsync(cancellationToken);
        return subtask;
    }

    public async Task<Subtask> UpdateSubtaskAsync(int id, Guid version, SubtaskInput input, CancellationToken cancellationToken = default)
    {
        ThrowIfInvalid(input.Title, input.StartDate, input.DueDate);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var subtask = await FindSubtaskAsync(db, id, cancellationToken);
        ExpectVersion(db, subtask, subtask.Version, version, "subtask");

        Apply(subtask, input, Now);
        await SaveEditAsync(db, "subtask", cancellationToken);
        return subtask;
    }

    public async Task MoveSubtaskAsync(int id, SubtaskStatus status, int index, string? reason = null, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var subtask = await FindSubtaskAsync(db, id, cancellationToken);

        var column = await db.Subtasks
            .Where(s => s.ProjectId == subtask.ProjectId && s.Status == status && s.Id != id)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);
        Renumber(ProjectMath.Reorder(column, subtask, index), (s, order) => s.SortOrder = order);

        var now = Now;
        if (subtask.Status != status)
        {
            var from = subtask.Status;
            subtask.Status = status;
            subtask.CompletedAt = status == SubtaskStatus.Done ? now : null;
            subtask.BlockedReason = status == SubtaskStatus.Blocked ? Reason(reason) : null;
            subtask.UpdatedAt = now;
            LogActivity(db, subtask.ProjectId, subtask.Id, ProjectMath.StatusChangeText(
                ProjectMath.DisplayName(from), ProjectMath.DisplayName(status), subtask.BlockedReason), now);
        }
        else if (status == SubtaskStatus.Blocked && Reason(reason) is { } newReason)
        {
            subtask.BlockedReason = newReason;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteSubtaskAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // The notes and links must be loaded: they're deleted by EF, not by the database (see the context).
        var subtask = await db.Subtasks
            .Include(s => s.Notes)
            .Include(s => s.Links)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw NotFound("Subtask", id);

        db.Subtasks.Remove(subtask);
        LogActivity(db, subtask.ProjectId, null, $"Deleted subtask \"{subtask.Title}\"", Now);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Subtask>> GetDueSubtasksAsync(CancellationToken cancellationToken = default)
    {
        var today = Today;

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Subtasks.AsNoTracking()
            .Include(s => s.Project)
            .Where(s => s.DueDate <= today && s.Status != SubtaskStatus.Done && s.Project!.ArchivedAt == null)
            .OrderBy(s => s.DueDate).ThenBy(s => s.ProjectId).ThenBy(s => s.SortOrder)
            .ToListAsync(cancellationToken);
    }

    public async Task<Note> AddProjectNoteAsync(int projectId, string body, CancellationToken cancellationToken = default)
    {
        var text = RequireBody(body);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            throw NotFound("Project", projectId);
        }

        var note = new Note { ProjectId = projectId, Kind = NoteKind.Entry, Body = text, CreatedAt = Now };
        db.Notes.Add(note);
        await db.SaveChangesAsync(cancellationToken);
        return note;
    }

    public async Task<Note> AddSubtaskNoteAsync(int subtaskId, string body, CancellationToken cancellationToken = default)
    {
        var text = RequireBody(body);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var projectId = await ProjectIdOfSubtaskAsync(db, subtaskId, cancellationToken);

        var note = new Note { ProjectId = projectId, SubtaskId = subtaskId, Kind = NoteKind.Entry, Body = text, CreatedAt = Now };
        db.Notes.Add(note);
        await db.SaveChangesAsync(cancellationToken);
        return note;
    }

    public async Task<Note> UpdateNoteAsync(int id, string body, CancellationToken cancellationToken = default)
    {
        var text = RequireBody(body);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id, cancellationToken) ?? throw NotFound("Note", id);
        if (note.Kind != NoteKind.Entry)
        {
            throw new InvalidOperationException("Activity notes can't be edited.");
        }

        if (note.Body != text)
        {
            note.Body = text;
            note.EditedAt = Now;
            await db.SaveChangesAsync(cancellationToken);
        }

        return note;
    }

    public async Task DeleteNoteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var deleted = await db.Notes.Where(n => n.Id == id).ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            throw NotFound("Note", id);
        }
    }

    public async Task<Link> AddProjectLinkAsync(int projectId, string? label, string url, CancellationToken cancellationToken = default)
    {
        var link = NewLink(label, url);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            throw NotFound("Project", projectId);
        }

        link.ProjectId = projectId;
        db.Links.Add(link);
        await db.SaveChangesAsync(cancellationToken);
        return link;
    }

    public async Task<Link> AddSubtaskLinkAsync(int subtaskId, string? label, string url, CancellationToken cancellationToken = default)
    {
        var link = NewLink(label, url);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        link.ProjectId = await ProjectIdOfSubtaskAsync(db, subtaskId, cancellationToken);
        link.SubtaskId = subtaskId;
        db.Links.Add(link);
        await db.SaveChangesAsync(cancellationToken);
        return link;
    }

    public async Task DeleteLinkAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var deleted = await db.Links.Where(l => l.Id == id).ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            throw NotFound("Link", id);
        }
    }

    private static void Apply(Project project, ProjectInput input, DateTime now)
    {
        project.Title = input.Title.Trim();
        project.Description = ProjectMath.Clean(input.Description);
        project.Area = input.Area;
        project.Priority = input.Priority;
        project.StartDate = input.StartDate;
        project.DueDate = input.DueDate;
        project.UpdatedAt = now;
        project.Version = Guid.NewGuid();
    }

    private static void Apply(Subtask subtask, SubtaskInput input, DateTime now)
    {
        subtask.Title = input.Title.Trim();
        subtask.Description = ProjectMath.Clean(input.Description);
        subtask.Priority = input.Priority;
        subtask.StartDate = input.StartDate;
        subtask.DueDate = input.DueDate;
        subtask.UpdatedAt = now;
        subtask.Version = Guid.NewGuid();
    }

    private Link NewLink(string? label, string url)
    {
        var uri = ProjectMath.ParseLink(url) ?? throw new ArgumentException("Links must be full http or https URLs.", nameof(url));
        var text = ProjectMath.Clean(label) ?? uri.Host;
        return new Link
        {
            Label = text.Length > ProjectMath.MaxLabelLength ? text[..ProjectMath.MaxLabelLength] : text,
            Url = uri.OriginalString,
            CreatedAt = Now,
        };
    }

    private static void LogActivity(CommandCenterDbContext db, int projectId, int? subtaskId, string text, DateTime now) =>
        db.Notes.Add(new Note { ProjectId = projectId, SubtaskId = subtaskId, Kind = NoteKind.Activity, Body = text, CreatedAt = now });

    /// <summary>
    /// Rejects the edit if the caller's copy is out of date. Setting the original value also makes
    /// SaveChanges catch an edit that lands between this check and the save.
    /// </summary>
    private static void ExpectVersion<T>(CommandCenterDbContext db, T entity, Guid current, Guid expected, string what)
        where T : class
    {
        if (current != expected)
        {
            throw Conflict(what);
        }

        db.Entry(entity).Property<Guid>("Version").OriginalValue = expected;
    }

    private static async Task SaveEditAsync(CommandCenterDbContext db, string what, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Conflict(what);
        }
    }

    private static ConcurrencyConflictException Conflict(string what) =>
        new($"This {what} was changed somewhere else after you opened it. Reload to see the latest version.");

    private static void Renumber<T>(List<T> ordered, Action<T, int> setOrder)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            setOrder(ordered[i], i);
        }
    }

    private static async Task<int> NextProjectSortOrderAsync(CommandCenterDbContext db, ProjectStatus status, CancellationToken cancellationToken)
    {
        var last = await db.Projects
            .Where(p => p.Status == status && p.ArchivedAt == null)
            .MaxAsync(p => (int?)p.SortOrder, cancellationToken);
        return (last ?? -1) + 1;
    }

    private static async Task<Project> FindProjectAsync(CommandCenterDbContext db, int id, CancellationToken cancellationToken) =>
        await db.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw NotFound("Project", id);

    private static async Task<Subtask> FindSubtaskAsync(CommandCenterDbContext db, int id, CancellationToken cancellationToken) =>
        await db.Subtasks.FirstOrDefaultAsync(s => s.Id == id, cancellationToken) ?? throw NotFound("Subtask", id);

    private static async Task<int> ProjectIdOfSubtaskAsync(CommandCenterDbContext db, int subtaskId, CancellationToken cancellationToken)
    {
        var projectId = await db.Subtasks
            .Where(s => s.Id == subtaskId)
            .Select(s => (int?)s.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);
        return projectId ?? throw NotFound("Subtask", subtaskId);
    }

    private static void ThrowIfInvalid(string? title, DateOnly? startDate, DateOnly? dueDate)
    {
        var errors = ProjectMath.Validate(title, startDate, dueDate);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors));
        }
    }

    private static string RequireBody(string body) =>
        ProjectMath.Clean(body) ?? throw new ArgumentException("A note can't be empty.", nameof(body));

    private static string? Reason(string? reason)
    {
        var text = ProjectMath.Clean(reason);
        return text is { Length: > ProjectMath.MaxReasonLength } ? text[..ProjectMath.MaxReasonLength] : text;
    }

    private static KeyNotFoundException NotFound(string what, int id) => new($"{what} {id} was not found.");

    private static TimeZoneInfo ResolveZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }
}
