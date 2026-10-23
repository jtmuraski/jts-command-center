namespace JtsCommandCenter.Core.Projects;

/// <summary>Image uploads for project and subtask descriptions.</summary>
public interface IAttachmentService
{
    /// <summary>
    /// Stores an image for a project (and optionally one of its subtasks). Only PNG, JPEG, GIF, and WebP
    /// up to <see cref="AttachmentOptions.MaxBytes"/> are accepted, judged by the file's contents. Invalid
    /// files throw <see cref="ArgumentException"/>; a missing project or subtask throws
    /// <see cref="KeyNotFoundException"/>.
    /// </summary>
    Task<Attachment> AddImageAsync(int projectId, int? subtaskId, string? fileName, Stream content, CancellationToken cancellationToken = default);

    /// <summary>The attachment and its bytes, or null when it doesn't exist.</summary>
    Task<AttachmentContent?> OpenAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>Where attachment bytes are kept: a local folder in development, Azure Blob Storage in production.</summary>
public interface IAttachmentStore
{
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>The stored bytes, or null when nothing is stored under the key.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Removes the bytes. Does nothing when the key isn't there.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
