using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;

namespace DevFlow.Application.Dashboard;

public record TaskStatusCount(TaskItemStatus Status, int Count);

public record TaskPriorityCount(TaskPriority Priority, int Count);

public record DashboardSummary(
    int ProjectCount,
    int TaskCount,
    int CompletedTaskCount,
    int OverdueTaskCount,
    IReadOnlyList<TaskStatusCount> TasksByStatus,
    IReadOnlyList<TaskPriorityCount> TasksByPriority,
    IReadOnlyList<ActivityLog> RecentActivity,
    /// <summary>Most overdue first (oldest due date first), capped — see DashboardService.OverdueTaskListLimit.</summary>
    IReadOnlyList<TaskItem> OverdueTasks);
