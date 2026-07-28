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
}
