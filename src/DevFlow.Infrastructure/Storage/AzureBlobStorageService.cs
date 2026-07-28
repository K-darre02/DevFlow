using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using DevFlow.Application.Common;
using Microsoft.Extensions.Options;

namespace DevFlow.Infrastructure.Storage;

// The production backend — selected by DependencyInjection.AddApiServices
// whenever Storage:Azure:ConnectionString is configured, LocalFileBlobStorageService
// otherwise. Not exercised by this solution's automated tests: doing so
// would need either a live Azure Storage account or the Azurite emulator,
// neither of which is available in this sandbox (no Docker — same
// constraint documented for PostgreSQL elsewhere in this codebase). The
// abstraction (IBlobStorageService) and the security properties it has to
// uphold (private container, short-lived SAS downloads, no public URLs)
// are still real and reviewable here even though this class itself isn't
// covered by an automated test the way LocalFileBlobStorageService is.
public class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobContainerClient _containerClient;
    private readonly AzureBlobStorageOptions _options;

    public AzureBlobStorageService(IOptions<AzureBlobStorageOptions> options)
    {
        _options = options.Value;
        _containerClient = new BlobContainerClient(_options.ConnectionString, _options.ContainerName);
    }

    public async Task UploadAsync(string blobKey, Stream content, string contentType, CancellationToken cancellationToken)
    {
        // PublicAccessType.None: the container itself is never publicly
        // readable — the only way to read a blob is a short-lived SAS URL
        // (GetDownloadUrlAsync) minted after this API's own tenant/task
        // authorization checks pass. See "Never expose public blob URLs".
        await _containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        var blobClient = _containerClient.GetBlobClient(blobKey);
        await blobClient.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(string blobKey, CancellationToken cancellationToken)
    {
        var blobClient = _containerClient.GetBlobClient(blobKey);
        var download = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return download.Value.Content;
    }

    public async Task DeleteAsync(string blobKey, CancellationToken cancellationToken)
    {
        var blobClient = _containerClient.GetBlobClient(blobKey);
        await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    public Task<Uri> GetDownloadUrlAsync(string blobKey, string fileName, string contentType, CancellationToken cancellationToken)
    {
        var blobClient = _containerClient.GetBlobClient(blobKey);

        if (!blobClient.CanGenerateSasUri)
        {
            // Only account-key or user-delegation-key credentials can mint a
            // SAS locally — surfaced loudly rather than silently returning
            // a non-working URL, since this would otherwise only show up in
            // production against a misconfigured connection string.
            throw new InvalidOperationException(
                "The configured Azure Storage credentials cannot generate SAS URIs (an account key or user delegation key is required).");
        }

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _containerClient.Name,
            BlobName = blobKey,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(_options.DownloadUrlExpiryMinutes),
            ContentDisposition = $"attachment; filename=\"{fileName}\"",
            ContentType = contentType,
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        return Task.FromResult(blobClient.GenerateSasUri(sasBuilder));
    }
}
