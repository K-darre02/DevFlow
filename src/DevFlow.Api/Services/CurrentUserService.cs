using System.IdentityModel.Tokens.Jwt;
using DevFlow.Application.Common;
using DevFlow.Domain.Enums;

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

    public TenantRole? Role
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User?.FindFirst("role")?.Value;
            return Enum.TryParse<TenantRole>(value, out var role) ? role : null;
        }
    }

    private Guid? GetGuidClaim(string claimType)
    {
        var value = _httpContextAccessor.HttpContext?.User?.FindFirst(claimType)?.Value;
        return Guid.TryParse(value, out var guid) ? guid : null;
    }
}
