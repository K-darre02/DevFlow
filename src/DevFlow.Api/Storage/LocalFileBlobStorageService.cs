using DevFlow.Application.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace DevFlow.Api.Storage;

// Local-development substitute for AzureBlobStorageService — see
// docs/devflow/01-architecture.md §9. Selected by DependencyInjection
// whenever Storage:Azure:ConnectionString isn't configured, which is the
// default in this sandbox (no live Azure account, no Docker for Azurite).
// This is the implementation this solution's automated tests actually run
// against.
public class LocalFileBlobStorageService : IBlobStorageService
{
    private readonly LocalFileStorageOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public LocalFileBlobStorageService(
        IOptions<LocalFileStorageOptions> options,
        IWebHostEnvironment environment,
        IHttpContextAccessor httpContextAccessor)
    {
        _options = options.Value;
        _environment = environment;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task UploadAsync(string blobKey, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var path = ResolvePath(blobKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
        await content.CopyToAsync(fileStream, cancellationToken);
    }

    public Task<Stream> OpenReadAsync(string blobKey, CancellationToken cancellationToken)
    {
        var path = ResolvePath(blobKey);

        // File.Exists false-negatives are impossible here in a way that
        // matters (TOCTOU aside, which isn't a real concern for read-only
        // access) — checked explicitly so a missing *directory* (a blob key
        // that was never uploaded) throws the same FileNotFoundException a
        // missing *file* would, rather than DirectoryNotFoundException.
        // Callers (BlobDownloadsController) only need to handle one
        // "doesn't exist" exception type this way.
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No blob found for key '{blobKey}'.", path);
        }

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string blobKey, CancellationToken cancellationToken)
    {
        var path = ResolvePath(blobKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<Uri> GetDownloadUrlAsync(string blobKey, string fileName, string contentType, CancellationToken cancellationToken)
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(_options.DownloadUrlExpiryMinutes).ToUnixTimeSeconds();
        var signature = SignedBlobUrl.Sign(_options.SigningKey, blobKey, fileName, contentType, expires);

        var request = _httpContextAccessor.HttpContext?.Request
            ?? throw new InvalidOperationException("GetDownloadUrlAsync requires an active HTTP request.");

        var query = QueryString.Create(new Dictionary<string, string?>
        {
            ["blobKey"] = blobKey,
            ["fileName"] = fileName,
            ["contentType"] = contentType,
            ["expires"] = expires.ToString(),
            ["sig"] = signature,
        });

        var uri = new Uri($"{request.Scheme}://{request.Host}/api/blob-downloads{query.Value}");
        return Task.FromResult(uri);
    }

    private string ResolvePath(string blobKey)
    {
        // blobKey only ever comes from AttachmentService's own generation
        // (tenantId/taskId/attachmentId-based, never user input) or from a
        // signature that's already been verified by the time this runs —
        // but path traversal defenses cost nothing and a broken assumption
        // upstream shouldn't become an arbitrary-file-read bug downstream.
        if (blobKey.Contains("..") || Path.IsPathRooted(blobKey))
        {
            throw new InvalidOperationException($"Invalid blob key: '{blobKey}'.");
        }

        var root = Path.Combine(_environment.ContentRootPath, _options.RootPath);
        var segments = blobKey.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return Path.Combine(new[] { root }.Concat(segments).ToArray());
    }
}
