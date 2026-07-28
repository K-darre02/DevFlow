using System.IdentityModel.Tokens.Jwt;
using DevFlow.Application.Common;

namespace DevFlow.Api.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId => GetGuidClaim(JwtRegisteredClaimNames.Sub);

    public Guid? TenantId => GetGuidClaim("tenant_id");

    private Guid? GetGuidClaim(string claimType)
    {
        var value = _httpContextAccessor.HttpContext?.User?.FindFirst(claimType)?.Value;
        return Guid.TryParse(value, out var guid) ? guid : null;
    }
}
