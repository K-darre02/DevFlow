using System.Security.Cryptography;
using System.Text;

namespace DevFlow.Application.Common;

/// <summary>
/// Generates and hashes invitation tokens. Shared between TeamService
/// (generates + hashes when creating an invitation) and
/// AcceptInvitationInputValidator (hashes the client-supplied token to look
/// it up) — extracted specifically so those two can't drift into using
/// different hash algorithms and silently never matching.
/// </summary>
public static class InvitationTokens
{
    /// <summary>A cryptographically random, URL-safe token — returned to the caller exactly once, never persisted.</summary>
    public static string Generate()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>What actually gets stored (Invitation.TokenHash) and matched against.</summary>
    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
