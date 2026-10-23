using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using JtsCommandCenter.Core.Projects;
using JtsCommandCenter.Infrastructure.Data;

namespace JtsCommandCenter.Infrastructure.Projects;

/// <summary>Checks and stores uploaded images: bytes in the attachment store, details in the database.</summary>
internal sealed class AttachmentService(
    IDbContextFactory<CommandCenterDbContext> contextFactory,
    IAttachmentStore store,
    TimeProvider time,
    IOptions<AttachmentOptions> options,
    ILogger<AttachmentService> logger) : IAttachmentService
{
    private readonly long maxBytes = options.Value.MaxBytes;

    public async Task<Attachment> AddImageAsync(int projectId, int? subtaskId, string? fileName, Stream content, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            throw new KeyNotFoundException($"Project {projectId} was not found.");
        }

        if (subtaskId is int id && !await db.Subtasks.AnyAsync(s => s.Id == id && s.ProjectId == projectId, cancellationToken))
        {
            throw new KeyNotFoundException($"Subtask {id} was not found in project {projectId}.");
        }

        using var buffer = await ReadLimitedAsync(content, cancellationToken);
        var contentType = ImageFormats.Detect(buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, ImageFormats.HeaderLength)))
            ?? throw new ArgumentException("Only PNG, JPEG, GIF, and WebP images can be attached.");

        var attachmentId = Guid.NewGuid();
        var attachment = new Attachment
        {
            Id = attachmentId,
            ProjectId = projectId,
            SubtaskId = subtaskId,
            FileName = ImageFormats.CleanFileName(fileName),
            ContentType = contentType,
            Size = buffer.Length,
            StorageKey = attachmentId.ToString("N"),
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };

        buffer.Position = 0;
        await store.SaveAsync(attachment.StorageKey, buffer, contentType, cancellationToken);
        try
        {
            db.Attachments.Add(attachment);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await store.DeleteAsync(attachment.StorageKey, CancellationToken.None);
            throw;
        }

        return attachment;
    }

    public async Task<AttachmentContent?> OpenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var attachment = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (attachment is null)
        {
            return null;
        }

        var content = await store.OpenReadAsync(attachment.StorageKey, cancellationToken);
        if (content is null)
        {
            logger.LogWarning("Attachment {AttachmentId} has no stored bytes", id);
            return null;
        }

        return new AttachmentContent(attachment, content);
    }

    /// <summary>Copies the upload into memory, refusing anything over the size limit without reading it all.</summary>
    private async Task<MemoryStream> ReadLimitedAsync(Stream content, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                await buffer.DisposeAsync();
                throw new ArgumentException($"Images can be at most {maxBytes / (1024 * 1024)} MB.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer;
    }
}
