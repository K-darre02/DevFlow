using System.IdentityModel.Tokens.Jwt;
using DevFlow.Api.Services;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DevFlow.IntegrationTests.Auth;

public class JwtTokenServiceTests
{
    private static JwtTokenService CreateService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "DevFlow.Tests",
                ["Jwt:Audience"] = "DevFlow.Tests",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:SigningKey"] = "test-signing-key-at-least-32-bytes-long-for-hmac-sha256"
            })
            .Build();

        return new JwtTokenService(configuration);
    }

    [Fact]
    public void GenerateToken_includes_subject_tenant_role_and_email_claims()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();
        var user = new User { Email = "person@example.com" };

        var (accessToken, expiresAt) = service.GenerateToken(user, tenantId, TenantRole.Admin);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);

        jwt.Claims.Should().ContainSingle(c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == user.Id.ToString());
        jwt.Claims.Should().ContainSingle(c => c.Type == "tenant_id" && c.Value == tenantId.ToString());
        jwt.Claims.Should().ContainSingle(c => c.Type == "role" && c.Value == "Admin");
        jwt.Claims.Should().ContainSingle(c => c.Type == JwtRegisteredClaimNames.Email && c.Value == user.Email);
        expiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GenerateToken_throws_when_signing_key_is_not_configured()
    {
        var configuration = new ConfigurationBuilder().Build();
        var service = new JwtTokenService(configuration);
        var user = new User { Email = "person@example.com" };

        var act = () => service.GenerateToken(user, Guid.NewGuid(), TenantRole.Owner);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Jwt:SigningKey*");
    }
}
