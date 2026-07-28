using DevFlow.Application.Common;

namespace DevFlow.Application.Activities;

public interface IActivityLogService
{
    /// <summary>Newest first, tenant-scoped via the ambient query filter. Page is 1-based.</summary>
    Task<PagedResult<DevFlow.Domain.Entities.ActivityLog>> GetActivitiesAsync(ActivityLogQuery query, CancellationToken cancellationToken);
}
