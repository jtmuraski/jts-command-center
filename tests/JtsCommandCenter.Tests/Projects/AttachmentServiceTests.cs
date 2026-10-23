using JtsCommandCenter.Core.Projects;
using JtsCommandCenter.Infrastructure.Projects;
using JtsCommandCenter.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace JtsCommandCenter.Tests.Projects;

public sealed class AttachmentServiceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 23, 15, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase database = new();
    private readonly FakeAttachmentStore store = new();
    private readonly FakeTimeProvider time = new(Start);
    private readonly ProjectService projects;
    private readonly AttachmentService attachments;

    public AttachmentServiceTests()
    {
        projects = new ProjectService(database, time, Options.Create(new ProjectsOptions()), store, NullLogger<ProjectService>.Instance);
        attachments = new AttachmentService(database, store, time,
            Options.Create(new AttachmentOptions { MaxBytes = 1024 }), NullLogger<AttachmentService>.Instance);
    }

    public void Dispose() => database.Dispose();

    private static MemoryStream Png(int size = 64)
    {
        var bytes = new byte[size];
        ImageFormatsTests.PngHeader.CopyTo(bytes, 0);
        return new MemoryStream(bytes);
    }

    private Task<Project> NewProject() => projects.CreateProjectAsync(new ProjectInput("Workflow update", null, WorkArea.Work));

    [Fact]
    public async Task AddImage_StoresBytesAndDetails()
    {
        var project = await NewProject();

        var attachment = await attachments.AddImageAsync(project.Id, null, @"C:\Desktop\bug.png", Png());

        Assert.Equal("bug.png", attachment.FileName);
        Assert.Equal(ImageFormats.Png, attachment.ContentType);
        Assert.Equal(64, attachment.Size);
        Assert.Equal(Start.UtcDateTime, attachment.CreatedAt);
        Assert.Equal(64, store.Files[attachment.StorageKey].Bytes.Length);
        Assert.Equal(ImageFormats.Png, store.Files[attachment.StorageKey].ContentType);
    }

    [Fact]
    public async Task AddImage_UsesTheContentsNotTheName()
    {
        var project = await NewProject();
        var svg = new MemoryStream("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray());

        await Assert.ThrowsAsync<ArgumentException>(() => attachments.AddImageAsync(project.Id, null, "innocent.png", svg));

        Assert.Empty(store.Files);
    }

    [Fact]
    public async Task AddImage_TooLarge_IsRejected()
    {
        var project = await NewProject();

        var error = await Assert.ThrowsAsync<ArgumentException>(() => attachments.AddImageAsync(project.Id, null, "big.png", Png(2048)));

        Assert.Contains("at most", error.Message);
        Assert.Empty(store.Files);
    }

    [Fact]
    public async Task AddImage_SubtaskMustBelongToTheProject()
    {
        var project = await NewProject();
        var other = await projects.CreateProjectAsync(new ProjectInput("Other", null, WorkArea.Personal));
        var subtask = await projects.CreateSubtaskAsync(other.Id, new SubtaskInput("Elsewhere", null));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => attachments.AddImageAsync(project.Id, subtask.Id, "x.png", Png()));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => attachments.AddImageAsync(404, null, "x.png", Png()));
    }

    [Fact]
    public async Task Open_ReturnsTheBytes()
    {
        var project = await NewProject();
        var attachment = await attachments.AddImageAsync(project.Id, null, "bug.png", Png());

        await using var content = await attachments.OpenAsync(attachment.Id);

        Assert.NotNull(content);
        Assert.Equal(ImageFormats.Png, content.Attachment.ContentType);
        using var copy = new MemoryStream();
        await content.Content.CopyToAsync(copy);
        Assert.Equal(64, copy.Length);
    }

    [Fact]
    public async Task Open_Unknown_IsNull() => Assert.Null(await attachments.OpenAsync(Guid.NewGuid()));

    [Fact]
    public async Task DeletingASubtask_RemovesItsImagesOnly()
    {
        var project = await NewProject();
        var subtask = await projects.CreateSubtaskAsync(project.Id, new SubtaskInput("Engine updates", null));
        var onSubtask = await attachments.AddImageAsync(project.Id, subtask.Id, "engine.png", Png());
        var onProject = await attachments.AddImageAsync(project.Id, null, "overview.png", Png());

        await projects.DeleteSubtaskAsync(subtask.Id);

        Assert.Equal([onProject.StorageKey], store.Files.Keys);
        Assert.Null(await attachments.OpenAsync(onSubtask.Id));
        Assert.NotNull(await attachments.OpenAsync(onProject.Id));
    }

    [Fact]
    public async Task DeletingAProject_RemovesAllItsImages()
    {
        var project = await NewProject();
        var subtask = await projects.CreateSubtaskAsync(project.Id, new SubtaskInput("Engine updates", null));
        await attachments.AddImageAsync(project.Id, subtask.Id, "engine.png", Png());
        await attachments.AddImageAsync(project.Id, null, "overview.png", Png());

        await projects.ArchiveProjectAsync(project.Id);
        await projects.DeleteProjectAsync(project.Id);

        Assert.Empty(store.Files);
        await using var db = database.CreateDbContext();
        Assert.Equal(0, await db.Attachments.CountAsync());
    }
}
