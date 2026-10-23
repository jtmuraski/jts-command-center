namespace JtsCommandCenter.Core.Projects;

/// <summary>
/// An uploaded image used in a project's or subtask's Markdown (e.g. a bug screenshot). The bytes live
/// in an <see cref="IAttachmentStore"/>; this row holds what's needed to serve them.
/// </summary>
public sealed class Attachment
{
    /// <summary>Also the URL: /attachments/{Id}.</summary>
    public Guid Id { get; set; }

    public int ProjectId { get; set; }
    public int? SubtaskId { get; set; }

    /// <summary>The name it was uploaded with, for alt text.</summary>
    public string FileName { get; set; } = "";

    /// <summary>Detected from the file's contents, never taken from the browser.</summary>
    public string ContentType { get; set; } = "";

    public long Size { get; set; }

    /// <summary>Where the bytes are in the attachment store.</summary>
    public string StorageKey { get; set; } = "";

    public DateTime CreatedAt { get; set; }
}

/// <summary>An attachment and an open stream of its bytes. Dispose it to close the stream.</summary>
public sealed record AttachmentContent(Attachment Attachment, Stream Content) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
