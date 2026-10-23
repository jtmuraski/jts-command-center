using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using JtsCommandCenter.Core.Projects;

namespace JtsCommandCenter.Infrastructure.Projects;

/// <summary>
/// Keeps attachment bytes in a private Azure Blob Storage container. The app is the only reader: images
/// are served through its /attachments endpoint, never by blob URL.
/// </summary>
internal sealed class BlobAttachmentStore(BlobContainerClient container) : IAttachmentStore
{
    private readonly SemaphoreSlim createLock = new(1, 1);
    private bool containerReady;

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        await EnsureContainerAsync(cancellationToken);
        var options = new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } };
        await container.GetBlobClient(key).UploadAsync(content, options, cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var download = await container.GetBlobClient(key).DownloadStreamingAsync(cancellationToken: cancellationToken);
            return download.Value.Content;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        await container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (containerReady)
        {
            return;
        }

        await createLock.WaitAsync(cancellationToken);
        try
        {
            if (!containerReady)
            {
                // No public access: anonymous requests to the container are refused.
                await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
                containerReady = true;
            }
        }
        finally
        {
            createLock.Release();
        }
    }
}
