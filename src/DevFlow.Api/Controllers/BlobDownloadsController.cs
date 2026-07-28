using DevFlow.Api.Storage;
using DevFlow.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DevFlow.Api.Controllers;

// Deliberately anonymous: this is what a short-lived signed download URL
// (LocalFileBlobStorageService.GetDownloadUrlAsync) actually points at —
// the browser follows it as a plain navigation/redirect target, which can't
// carry an Authorization header. Authorization here comes entirely from the
// URL's own HMAC signature (SignedBlobUrl) instead of the normal JWT
// pipeline: a valid, unexpired signature *is* the credential, scoped to
// exactly one blob. Only ever reached when local storage is the active
// backend — Azure's own SAS URLs point directly at Azure and never touch
// this endpoint at all.
[ApiController]
[Route("api/blob-downloads")]
[AllowAnonymous]
public class BlobDownloadsController : ControllerBase
{
    private readonly IBlobStorageService _blobStorage;
    private readonly LocalFileStorageOptions _options;

    public BlobDownloadsController(IBlobStorageService blobStorage, IOptions<LocalFileStorageOptions> options)
    {
        _blobStorage = blobStorage;
        _options = options.Value;
    }

    [HttpGet]
    public async Task<IActionResult> Download(
        [FromQuery] string? blobKey,
        [FromQuery] string? fileName,
        [FromQuery] string? contentType,
        [FromQuery] long expires,
        [FromQuery] string? sig,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(blobKey) || string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(contentType) || string.IsNullOrEmpty(sig)
            || !SignedBlobUrl.Verify(_options.SigningKey, blobKey, fileName, contentType, expires, sig))
        {
            // Not NotFound: this must not distinguish "bad signature" from
            // "blob doesn't exist" any more than it has to — either way,
            // nothing is served.
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        Stream stream;
        try
        {
            stream = await _blobStorage.OpenReadAsync(blobKey, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }

        return File(stream, contentType, fileName);
    }
}
