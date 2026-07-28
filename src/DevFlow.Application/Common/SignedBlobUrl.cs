using System.Security.Cryptography;
using System.Text;

namespace DevFlow.Application.Common;

/// <summary>
/// HMAC-signs (blobKey, fileName, contentType, expiry) so a download link
/// can carry its own proof of authorization instead of a bearer token — the
/// same shape of problem InvitationTokens solves for invitation links, and
/// the reason a browser redirect to a download URL can work at all: by the
/// time the browser follows the redirect, there's no way to attach an
/// Authorization header, so the URL itself has to be the credential, valid
/// only for this one blob and only until it expires. Used by
/// LocalFileBlobStorageService (signs) and the blob-downloads passthrough
/// endpoint (verifies) — extracted here, not duplicated in each, so they
/// can't drift into disagreeing about what a valid signature looks like.
/// Azure's own SAS mechanism replaces this entirely for the Azure-backed
/// implementation; this is specifically the local-storage substitute.
/// </summary>
public static class SignedBlobUrl
{
    public static string Sign(string signingKey, string blobKey, string fileName, string contentType, long expiresUnixSeconds)
    {
        var payload = BuildPayload(blobKey, fileName, contentType, expiresUnixSeconds);
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey), Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>False if the signature doesn't match *or* expiresUnixSeconds is already in the past.</summary>
    public static bool Verify(string signingKey, string blobKey, string fileName, string contentType, long expiresUnixSeconds, string signature)
    {
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresUnixSeconds)
        {
            return false;
        }

        var expected = Sign(signingKey, blobKey, fileName, contentType, expiresUnixSeconds);

        // Constant-time comparison: this is a bearer credential (see class
        // summary) — a timing side-channel here would leak it byte by byte.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature));
    }

    private static string BuildPayload(string blobKey, string fileName, string contentType, long expiresUnixSeconds) =>
        $"{blobKey}|{fileName}|{contentType}|{expiresUnixSeconds}";
}
