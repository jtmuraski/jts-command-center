using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using JtsCommandCenter.Core.Projects;

namespace JtsCommandCenter.Infrastructure.Projects;

/// <summary>Keeps attachment bytes as files in a folder. For local development.</summary>
internal sealed partial class LocalAttachmentStore(IOptions<AttachmentOptions> options) : IAttachmentStore
{
    private readonly string root = Path.GetFullPath(options.Value.LocalPath);

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(root);
        await using var file = new FileStream(PathFor(key), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, cancellationToken);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        File.Delete(PathFor(key));
        return Task.CompletedTask;
    }

    /// <summary>Keys are generated ids (32 hex digits), which also rules out paths escaping the folder.</summary>
    private string PathFor(string key) =>
        KeyPattern().IsMatch(key) ? Path.Combine(root, key) : throw new ArgumentException($"Invalid attachment key '{key}'.", nameof(key));

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex KeyPattern();
}
