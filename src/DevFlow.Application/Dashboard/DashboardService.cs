using DevFlow.Application.Activities;
using DevFlow.Application.Common;
using DevFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DevFlow.Application.Dashboard;

public class DashboardService : IDashboardService
{
    // Small, fixed caps for "widget" data (recent activity / overdue task
    // lists) — a dashboard summary isn't a paginated listing endpoint; the
    // full lists already exist at /api/activity and /api/tasks?status=...
    private const int RecentActivityLimit = 5;
    private const int OverdueTaskLimit = 5;

    private readonly IApplicationDbContext _context;
    private readonly IActivityLogService _activityLogService;

    public DashboardService(IApplicationDbContext context, IActivityLogService activityLogService)
    {
        _context = context;
        _activityLogService = activityLogService;
    }

    public async Task<DashboardSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Sequentially awaited, deliberately not Task.WhenAll: every query
        // below shares the same scoped IApplicationDbContext, and EF Core's
        // DbContext isn't safe for concurrent use — running these in
        // parallel would throw "A second operation was started on this
        // context before a previous operation completed" the moment two
        // overlap. Each one is still a single, targeted COUNT/GROUP BY
        // pushed to SQL, never a full table pulled into memory to
        // aggregate client-side — that's what "optimized for reporting"
        // means here.
        var projectCount = await _context.Projects.CountAsync(p => !p.IsArchived, cancellationToken);
        var taskCount = await _context.TaskItems.CountAsync(cancellationToken);
        var completedTaskCount = await _context.TaskItems.CountAsync(t => t.Status == TaskItemStatus.Done, cancellationToken);
        var overdueTaskCount = await _context.TaskItems.CountAsync(
            t => t.DueDate != null && t.DueDate < today && t.Status != TaskItemStatus.Done, cancellationToken);

        var tasksByStatus = await _context.TaskItems
            .GroupBy(t => t.Status)
            .Select(g => new TaskStatusCount(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

        var tasksByPriority = await _context.TaskItems
            .GroupBy(t => t.Priority)
            .Select(g => new TaskPriorityCount(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

        var overdueTasks = await _context.TaskItems
            .AsNoTracking()
            .Include(t => t.Project)
            .Where(t => t.DueDate != null && t.DueDate < today && t.Status != TaskItemStatus.Done)
            .OrderBy(t => t.DueDate)
            .Take(OverdueTaskLimit)
            .ToListAsync(cancellationToken);

        // Reuses ActivityLogService rather than re-querying ActivityLogs
        // directly — same ordering/paging logic (including the
        // CreatedAtTicks SQLite workaround), one place that knows how to do it.
        var recentActivity = await _activityLogService.GetActivitiesAsync(
            new ActivityLogQuery(1, RecentActivityLimit, null, null, null), cancellationToken);

        return new DashboardSummary(
            projectCount,
            taskCount,
            completedTaskCount,
            overdueTaskCount,
            tasksByStatus,
            tasksByPriority,
            recentActivity.Items,
            overdueTasks);
    }
}
