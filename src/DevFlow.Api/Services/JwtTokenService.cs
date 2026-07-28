using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using Microsoft.IdentityModel.Tokens;

namespace DevFlow.Api.Services;

public class JwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // tenantId/role are explicit parameters rather than read off User: User
    // is tenant-independent (TenantMember is the join), and a token is
    // always issued for one specific tenant membership — the caller (Auth/
    // TeamController) is the one that knows which membership this is.
    public (string AccessToken, DateTimeOffset ExpiresAt) GenerateToken(User user, Guid tenantId, TenantRole role)
    {
        var jwtSection = _configuration.GetSection("Jwt");

        var signingKey = jwtSection["SigningKey"]
            ?? throw new InvalidOperationException(
                "Jwt:SigningKey is not configured. Set it via `dotnet user-secrets set \"Jwt:SigningKey\" \"<value>\" --project src/DevFlow.Api`.");

        var accessTokenMinutes = jwtSection.GetValue("AccessTokenMinutes", 15);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(accessTokenMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("role", role.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwtSection["Issuer"],
            audience: jwtSection["Audience"],
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        return (accessToken, expiresAt);
    }
}
