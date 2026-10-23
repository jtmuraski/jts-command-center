using JtsCommandCenter.Core.Projects;
using JtsCommandCenter.Infrastructure.Data;
using JtsCommandCenter.Infrastructure.Projects;
using JtsCommandCenter.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace JtsCommandCenter.Tests.Projects;

public sealed class ProjectServiceTests : IDisposable
{
    // 03:00 UTC on Oct 24 is still the evening of Oct 23 in Chicago.
    private static readonly DateTimeOffset Start = new(2026, 10, 24, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly ChicagoToday = new(2026, 10, 23);

    private readonly TestDatabase database = new();
    private readonly FakeTimeProvider time = new(Start);
    private readonly ProjectService service;

    public ProjectServiceTests() =>
        service = new ProjectService(database, time, Options.Create(new ProjectsOptions { TimeZone = "America/Chicago" }));

    public void Dispose() => database.Dispose();

    private Task<Project> NewProject(string title = "Workflow update", ProjectStatus status = ProjectStatus.Planned, WorkArea area = WorkArea.Work) =>
        service.CreateProjectAsync(new ProjectInput(title, null, area), status);

    private Task<Subtask> NewSubtask(int projectId, string title, SubtaskStatus status = SubtaskStatus.ToDo, DateOnly? due = null) =>
        service.CreateSubtaskAsync(projectId, new SubtaskInput(title, null, DueDate: due), status);

    private async Task<List<Note>> NotesOf(int projectId) => (await service.GetProjectAsync(projectId))!.Notes;

    [Fact]
    public async Task CreateProject_TrimsAndStampsIt()
    {
        var created = await service.CreateProjectAsync(
            new ProjectInput("  Workflow update  ", "  ", WorkArea.Work, Priority.High, ChicagoToday, ChicagoToday.AddDays(60)));

        var project = (await service.GetProjectAsync(created.Id))!;
        Assert.Equal("Workflow update", project.Title);
        Assert.Null(project.Description);
        Assert.Equal(Priority.High, project.Priority);
        Assert.Equal(ProjectStatus.Planned, project.Status);
        Assert.Equal(ChicagoToday.AddDays(60), project.DueDate);
        Assert.Equal(Start.UtcDateTime, project.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, project.CreatedAt.Kind);
        Assert.NotEqual(Guid.Empty, project.Version);
    }

    [Fact]
    public async Task CreateProject_GoesToTheBottomOfItsColumn()
    {
        var first = await NewProject("First");
        var second = await NewProject("Second");
        var active = await NewProject("Elsewhere", ProjectStatus.Active);

        Assert.Equal(0, first.SortOrder);
        Assert.Equal(1, second.SortOrder);
        Assert.Equal(0, active.SortOrder);
    }

    [Fact]
    public async Task CreateProject_Invalid_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProjectAsync(new ProjectInput(" ", null, WorkArea.Personal)));

    [Fact]
    public async Task CreateProject_AsDone_IsCompleted()
    {
        var project = await NewProject(status: ProjectStatus.Done);

        Assert.Equal(Start.UtcDateTime, project.CompletedAt);
    }

    [Fact]
    public async Task UpdateProject_WithCurrentVersion_SavesAndBumpsVersion()
    {
        var project = await NewProject();
        time.Advance(TimeSpan.FromHours(1));

        var updated = await service.UpdateProjectAsync(project.Id, project.Version,
            new ProjectInput("Workflow update v2", "**Engine**, model, workflow", WorkArea.Work));

        var saved = (await service.GetProjectAsync(project.Id))!;
        Assert.Equal("Workflow update v2", saved.Title);
        Assert.Equal("**Engine**, model, workflow", saved.Description);
        Assert.Equal(Start.UtcDateTime.AddHours(1), saved.UpdatedAt);
        Assert.NotEqual(project.Version, updated.Version);
        Assert.Equal(updated.Version, saved.Version);
    }

    [Fact]
    public async Task UpdateProject_WithStaleVersion_IsRejected()
    {
        var project = await NewProject();
        await service.UpdateProjectAsync(project.Id, project.Version, new ProjectInput("Edited on the laptop", null, WorkArea.Work));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            service.UpdateProjectAsync(project.Id, project.Version, new ProjectInput("Edited on the tablet", null, WorkArea.Work)));

        Assert.Equal("Edited on the laptop", (await service.GetProjectAsync(project.Id))!.Title);
    }

    [Fact]
    public async Task UpdateProject_Missing_Throws() =>
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.UpdateProjectAsync(404, Guid.NewGuid(), new ProjectInput("Nope", null, WorkArea.Work)));

    [Fact]
    public async Task MoveProject_ReordersTheTargetColumn()
    {
        var a = await NewProject("A", ProjectStatus.Active);
        var b = await NewProject("B", ProjectStatus.Active);
        var moving = await NewProject("Moving");

        await service.MoveProjectAsync(moving.Id, ProjectStatus.Active, 1);

        var active = (await service.GetProjectsAsync()).Where(p => p.Status == ProjectStatus.Active).Select(p => p.Title);
        Assert.Equal(["A", "Moving", "B"], active);

        await service.MoveProjectAsync(a.Id, ProjectStatus.Active, 99);
        active = (await service.GetProjectsAsync()).Where(p => p.Status == ProjectStatus.Active).Select(p => p.Title);
        Assert.Equal(["Moving", "B", "A"], active);
        Assert.NotNull(b);
    }

    [Fact]
    public async Task MoveProject_ToDoneAndBack_SetsAndClearsCompletedAndLogsIt()
    {
        var project = await NewProject(status: ProjectStatus.Active);

        await service.MoveProjectAsync(project.Id, ProjectStatus.Done, 0);
        Assert.Equal(Start.UtcDateTime, (await service.GetProjectAsync(project.Id))!.CompletedAt);

        await service.MoveProjectAsync(project.Id, ProjectStatus.Active, 0);
        var reopened = (await service.GetProjectAsync(project.Id))!;
        Assert.Null(reopened.CompletedAt);
        Assert.Equal(["Moved from Done to Active", "Moved from Active to Done"], reopened.Notes.Select(n => n.Body));
        Assert.All(reopened.Notes, n => Assert.Equal(NoteKind.Activity, n.Kind));
    }

    [Fact]
    public async Task MoveProject_OnHold_KeepsReasonUntilItLeaves()
    {
        var project = await NewProject(status: ProjectStatus.Active);

        await service.MoveProjectAsync(project.Id, ProjectStatus.OnHold, 0, "  Waiting on budget approval ");
        var held = (await service.GetProjectAsync(project.Id))!;
        Assert.Equal("Waiting on budget approval", held.BlockedReason);
        Assert.Equal("Moved from Active to On Hold: Waiting on budget approval", held.Notes[0].Body);

        await service.MoveProjectAsync(project.Id, ProjectStatus.Active, 0);
        Assert.Null((await service.GetProjectAsync(project.Id))!.BlockedReason);
    }

    [Fact]
    public async Task MoveProject_DoesNotInvalidateAnOpenEdit()
    {
        var project = await NewProject();

        await service.MoveProjectAsync(project.Id, ProjectStatus.Active, 0);

        await service.UpdateProjectAsync(project.Id, project.Version, new ProjectInput("Still editable", null, WorkArea.Work));
    }

    [Fact]
    public async Task ArchiveAndRestore_MovesProjectOffAndBackOntoTheBoard()
    {
        var project = await NewProject();
        await NewProject("Other");

        await service.ArchiveProjectAsync(project.Id);
        Assert.Equal(["Other"], (await service.GetProjectsAsync()).Select(p => p.Title));
        Assert.Equal(["Workflow update"], (await service.GetProjectsAsync(archived: true)).Select(p => p.Title));

        await service.RestoreProjectAsync(project.Id);
        Assert.Equal(["Other", "Workflow update"], (await service.GetProjectsAsync()).Select(p => p.Title));
        Assert.Equal(["Restored from the archive", "Archived"], (await NotesOf(project.Id)).Select(n => n.Body));
    }

    [Fact]
    public async Task DeleteProject_MustBeArchivedFirst()
    {
        var project = await NewProject();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteProjectAsync(project.Id));
        Assert.NotNull(await service.GetProjectAsync(project.Id));
    }

    [Fact]
    public async Task DeleteProject_RemovesEverythingInIt()
    {
        var project = await NewProject();
        var subtask = await NewSubtask(project.Id, "Engine updates");
        await service.AddSubtaskNoteAsync(subtask.Id, "Started on the engine");
        await service.AddSubtaskLinkAsync(subtask.Id, "Spec", "https://example.com/spec");
        await service.AddProjectNoteAsync(project.Id, "Kickoff");
        await service.AddProjectLinkAsync(project.Id, null, "https://example.com/board");

        await service.ArchiveProjectAsync(project.Id);
        await service.DeleteProjectAsync(project.Id);

        await using var db = database.CreateDbContext();
        Assert.Equal(0, await db.Projects.CountAsync());
        Assert.Equal(0, await db.Subtasks.CountAsync());
        Assert.Equal(0, await db.Notes.CountAsync());
        Assert.Equal(0, await db.Links.CountAsync());
    }

    [Fact]
    public async Task CreateSubtask_AddsToItsColumnAndLogsOnTheProject()
    {
        var project = await NewProject();

        var engine = await NewSubtask(project.Id, "Engine updates");
        var model = await NewSubtask(project.Id, "Model updates");
        var other = await NewSubtask((await NewProject("Other")).Id, "Elsewhere");

        Assert.Equal(0, engine.SortOrder);
        Assert.Equal(1, model.SortOrder);
        Assert.Equal(0, other.SortOrder);

        var saved = (await service.GetProjectAsync(project.Id))!;
        Assert.Equal(["Engine updates", "Model updates"], saved.Subtasks.Select(s => s.Title));
        Assert.Equal(["Added subtask \"Model updates\"", "Added subtask \"Engine updates\""], saved.Notes.Select(n => n.Body));
    }

    [Fact]
    public async Task CreateSubtask_MissingProject_Throws() =>
        await Assert.ThrowsAsync<KeyNotFoundException>(() => NewSubtask(404, "Orphan"));

    [Fact]
    public async Task MoveSubtask_StaysWithinItsProjectAndTracksStatus()
    {
        var project = await NewProject();
        var engine = await NewSubtask(project.Id, "Engine updates");
        var model = await NewSubtask(project.Id, "Model updates", SubtaskStatus.InProgress);
        await NewSubtask((await NewProject("Other")).Id, "Other in progress", SubtaskStatus.InProgress);

        await service.MoveSubtaskAsync(engine.Id, SubtaskStatus.InProgress, 0);
        var inProgress = (await service.GetProjectAsync(project.Id))!.Subtasks.Where(s => s.Status == SubtaskStatus.InProgress);
        Assert.Equal(["Engine updates", "Model updates"], inProgress.Select(s => s.Title));

        await service.MoveSubtaskAsync(model.Id, SubtaskStatus.Blocked, 0, "RFI submitted to management");
        var blocked = (await service.GetSubtaskAsync(model.Id))!;
        Assert.Equal("RFI submitted to management", blocked.BlockedReason);
        Assert.Equal("Moved from In Progress to Blocked: RFI submitted to management", blocked.Notes[0].Body);
        Assert.Equal(project.Id, blocked.Notes[0].ProjectId);

        await service.MoveSubtaskAsync(model.Id, SubtaskStatus.Done, 0);
        var done = (await service.GetSubtaskAsync(model.Id))!;
        Assert.Null(done.BlockedReason);
        Assert.Equal(Start.UtcDateTime, done.CompletedAt);
        Assert.Equal(new ProjectProgress(1, 2), ProjectMath.ProgressOf((await service.GetProjectAsync(project.Id))!.Subtasks));
    }

    [Fact]
    public async Task MoveSubtask_SameColumn_UpdatesReasonWithoutLogging()
    {
        var project = await NewProject();
        var subtask = await NewSubtask(project.Id, "Engine updates", SubtaskStatus.Blocked);

        await service.MoveSubtaskAsync(subtask.Id, SubtaskStatus.Blocked, 0, "Waiting on vendor");

        var saved = (await service.GetSubtaskAsync(subtask.Id))!;
        Assert.Equal("Waiting on vendor", saved.BlockedReason);
        Assert.Empty(saved.Notes);
    }

    [Fact]
    public async Task UpdateSubtask_WithStaleVersion_IsRejected()
    {
        var project = await NewProject();
        var subtask = await NewSubtask(project.Id, "Engine updates");
        await service.UpdateSubtaskAsync(subtask.Id, subtask.Version, new SubtaskInput("Engine updates (phase 1)", null));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            service.UpdateSubtaskAsync(subtask.Id, subtask.Version, new SubtaskInput("Stale", null)));
    }

    [Fact]
    public async Task DeleteSubtask_RemovesItsNotesAndLinksAndLogsIt()
    {
        var project = await NewProject();
        var subtask = await NewSubtask(project.Id, "Engine updates");
        await service.AddSubtaskNoteAsync(subtask.Id, "Halfway there");
        await service.AddSubtaskLinkAsync(subtask.Id, null, "https://example.com/engine");

        await service.DeleteSubtaskAsync(subtask.Id);

        var saved = (await service.GetProjectAsync(project.Id))!;
        Assert.Empty(saved.Subtasks);
        Assert.Empty(saved.Links);
        Assert.Equal(["Deleted subtask \"Engine updates\"", "Added subtask \"Engine updates\""], saved.Notes.Select(n => n.Body));
    }

    [Fact]
    public async Task GetDueSubtasks_UsesTheConfiguredTimeZonesToday()
    {
        var project = await NewProject();
        await NewSubtask(project.Id, "Overdue", due: ChicagoToday.AddDays(-2));
        await NewSubtask(project.Id, "Due today", due: ChicagoToday);
        await NewSubtask(project.Id, "Due tomorrow", due: ChicagoToday.AddDays(1));
        await NewSubtask(project.Id, "No date");
        await NewSubtask(project.Id, "Already done", SubtaskStatus.Done, ChicagoToday);
        var archived = await NewProject("Old", area: WorkArea.Personal);
        await NewSubtask(archived.Id, "In an archived project", due: ChicagoToday);
        await service.ArchiveProjectAsync(archived.Id);

        var due = await service.GetDueSubtasksAsync();

        Assert.Equal(["Overdue", "Due today"], due.Select(s => s.Title));
        Assert.All(due, s => Assert.Equal("Workflow update", s.Project!.Title));
    }

    [Fact]
    public async Task Notes_CanBeAddedEditedAndDeleted()
    {
        var project = await NewProject();
        var note = await service.AddProjectNoteAsync(project.Id, "  RFI submitted to management. Waiting for clarification.  ");
        Assert.Equal("RFI submitted to management. Waiting for clarification.", note.Body);
        Assert.Equal(NoteKind.Entry, note.Kind);
        Assert.Null(note.EditedAt);

        time.Advance(TimeSpan.FromDays(1));
        var edited = await service.UpdateNoteAsync(note.Id, "RFI answered.");
        Assert.Equal("RFI answered.", edited.Body);
        Assert.Equal(Start.UtcDateTime.AddDays(1), edited.EditedAt);

        await service.DeleteNoteAsync(note.Id);
        Assert.Empty(await NotesOf(project.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DeleteNoteAsync(note.Id));
    }

    [Fact]
    public async Task Notes_ListNewestFirst()
    {
        var project = await NewProject();
        await service.AddProjectNoteAsync(project.Id, "First");
        time.Advance(TimeSpan.FromMinutes(5));
        await service.AddProjectNoteAsync(project.Id, "Second");

        Assert.Equal(["Second", "First"], (await NotesOf(project.Id)).Select(n => n.Body));
    }

    [Fact]
    public async Task Notes_EmptyBody_IsRejected()
    {
        var project = await NewProject();

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddProjectNoteAsync(project.Id, "   "));
    }

    [Fact]
    public async Task Notes_ActivityCannotBeEdited()
    {
        var project = await NewProject();
        await service.MoveProjectAsync(project.Id, ProjectStatus.Active, 0);
        var activity = (await NotesOf(project.Id)).Single();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateNoteAsync(activity.Id, "Rewritten"));
    }

    [Fact]
    public async Task SubtaskNotes_ShowOnTheSubtaskAndTheProjectLog()
    {
        var project = await NewProject();
        var subtask = await NewSubtask(project.Id, "Model updates");

        var note = await service.AddSubtaskNoteAsync(subtask.Id, "Retrained on October data");

        Assert.Equal(project.Id, note.ProjectId);
        Assert.Equal(["Retrained on October data"], (await service.GetSubtaskAsync(subtask.Id))!.Notes.Select(n => n.Body));
        Assert.Contains(await NotesOf(project.Id), n => n.Id == note.Id && n.SubtaskId == subtask.Id);
    }

    [Fact]
    public async Task Links_DefaultLabelToHostAndRejectBadUrls()
    {
        var project = await NewProject();

        var link = await service.AddProjectLinkAsync(project.Id, " ", "https://dev.azure.com/org/_workitems/edit/42");
        Assert.Equal("dev.azure.com", link.Label);

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddProjectLinkAsync(project.Id, "Bad", "javascript:alert(1)"));

        await service.DeleteLinkAsync(link.Id);
        Assert.Empty((await service.GetProjectAsync(project.Id))!.Links);
    }

    [Fact]
    public async Task SubtaskLinks_BelongToTheProjectToo()
    {
        var project = await NewProject();
        var subtask = await NewSubtask(project.Id, "Workflow updates");

        var link = await service.AddSubtaskLinkAsync(subtask.Id, "Ticket", "https://example.com/ticket/7");

        Assert.Equal(project.Id, link.ProjectId);
        Assert.Equal(["Ticket"], (await service.GetSubtaskAsync(subtask.Id))!.Links.Select(l => l.Label));
    }

    [Fact]
    public void Migrations_MatchTheModel()
    {
        // Fails when the model changes without a new migration (dotnet ef migrations add <Name>).
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);

        Assert.False(db.Database.HasPendingModelChanges());
    }
}
