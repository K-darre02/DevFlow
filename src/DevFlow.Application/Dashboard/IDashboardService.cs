namespace DevFlow.Application.Dashboard;

public interface IDashboardService
{
    /// <summary>Tenant-scoped via the ambient query filter, same as every other service — no dashboard-specific isolation logic needed.</summary>
    Task<DashboardSummary> GetSummaryAsync(CancellationToken cancellationToken);
}
