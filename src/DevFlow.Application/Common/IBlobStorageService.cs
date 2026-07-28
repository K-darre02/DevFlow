namespace DevFlow.Application.Common;

/// <summary>
/// Abstraction over file storage — implemented by AzureBlobStorageService
/// (DevFlow.Infrastructure, the production/Azure Blob Storage backend) and
/// LocalFileBlobStorageService (DevFlow.Api, the local-development
/// substitute — see docs/devflow/01-architecture.md §9's table of local
/// stand-ins for cloud services). AttachmentService only ever talks to this
/// interface, never to a specific backend.
/// </summary>
public interface IBlobStorageService
{
    Task UploadAsync(string blobKey, Stream content, string contentType, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string blobKey, CancellationToken cancellationToken);

    Task DeleteAsync(string blobKey, CancellationToken cancellationToken);

    /// <summary>
    /// A short-lived (implementation-defined expiry, a handful of minutes),
    /// single-use-in-spirit URL a client can download the blob from
    /// directly — never a permanent/public blob URL. fileName/contentType
    /// let the generated URL carry the right download headers
    /// (Content-Disposition, Content-Type) without a second lookup.
    /// </summary>
    Task<Uri> GetDownloadUrlAsync(string blobKey, string fileName, string contentType, CancellationToken cancellationToken);
}
