using JtsCommandCenter.Core.Projects;

namespace JtsCommandCenter.Tests.Support;

/// <summary>Keeps attachment bytes in memory so services can be tested without a disk or Azure.</summary>
internal sealed class FakeAttachmentStore : IAttachmentStore
{
    public Dictionary<string, (byte[] Bytes, string ContentType)> Files { get; } = [];

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        Files[key] = (copy.ToArray(), contentType);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream?>(Files.TryGetValue(key, out var file) ? new MemoryStream(file.Bytes) : null);

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        Files.Remove(key);
        return Task.CompletedTask;
    }
}
