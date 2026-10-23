namespace JtsCommandCenter.Core.Projects;

/// <summary>Settings bound from the "Attachments" section of appsettings.json.</summary>
public sealed class AttachmentOptions
{
    public const string SectionName = "Attachments";
    public const string LocalProvider = "Local";
    public const string AzureBlobProvider = "AzureBlob";

    /// <summary>"Local" (a folder, for development) or "AzureBlob".</summary>
    public string Provider { get; set; } = LocalProvider;

    /// <summary>Folder for the Local provider. A relative path is resolved against the app's content root.</summary>
    public string LocalPath { get; set; } = "App_Data/attachments";

    /// <summary>e.g. https://myaccount.blob.core.windows.net. The app signs in with its managed identity.</summary>
    public string? BlobServiceUri { get; set; }

    /// <summary>A private container (no public access); images are served through the app.</summary>
    public string ContainerName { get; set; } = "attachments";

    public long MaxBytes { get; set; } = 10 * 1024 * 1024;
}
