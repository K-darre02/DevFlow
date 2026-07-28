using DevFlow.Domain.Enums;

namespace DevFlow.Application.Common;

/// <summary>
/// Resolves the authenticated user/tenant for the current request from claims.
/// Implemented in the Api layer (backed by IHttpContextAccessor) and consumed
/// by Infrastructure (DevFlowDbContext's tenant query filters) without either
/// layer depending on ASP.NET Core specifics directly — see
/// docs/devflow/04-security.md §2 (tenant isolation).
/// </summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }

    Guid? TenantId { get; }

    /// <summary>
    /// The caller's role within the active tenant, from the JWT's "role"
    /// claim. Like TenantId, this reflects whatever was true when the token
    /// was issued — a role change takes up to the access-token lifetime
    /// (15 min) to be reflected in a *new* token; see
    /// docs/devflow/04-security.md §4.
    /// </summary>
    TenantRole? Role { get; }
}
