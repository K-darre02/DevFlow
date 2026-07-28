using DevFlow.Application.Common;

namespace DevFlow.IntegrationTests.TestSupport;

// An in-memory IBlobStorageService for Application-layer tests
// (AttachmentServiceTests) that don't need real file I/O — LocalFileBlobStorageServiceTests
// covers the real filesystem-backed implementation directly.
public sealed class FakeBlobStorageService : IBlobStorageService
{
    public Dictionary<string, byte[]> Blobs { get; } = new();
    public List<string> DeletedKeys { get; } = new();

    public async Task UploadAsync(string blobKey, Stream content, string contentType, CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        await content.CopyToAsync(memoryStream, cancellationToken);
        Blobs[blobKey] = memoryStream.ToArray();
    }

    public Task<Stream> OpenReadAsync(string blobKey, CancellationToken cancellationToken)
    {
        if (!Blobs.TryGetValue(blobKey, out var bytes))
        {
            throw new FileNotFoundException($"No blob found for key '{blobKey}'.");
        }

        return Task.FromResult<Stream>(new MemoryStream(bytes));
    }

    public Task DeleteAsync(string blobKey, CancellationToken cancellationToken)
    {
        Blobs.Remove(blobKey);
        DeletedKeys.Add(blobKey);
        return Task.CompletedTask;
    }

    public Task<Uri> GetDownloadUrlAsync(string blobKey, string fileName, string contentType, CancellationToken cancellationToken)
    {
        return Task.FromResult(new Uri($"https://example.test/fake-download/{Uri.EscapeDataString(blobKey)}"));
    }
}
