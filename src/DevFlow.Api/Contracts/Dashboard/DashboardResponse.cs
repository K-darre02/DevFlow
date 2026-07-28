using DevFlow.Api.Contracts.Activity;
using DevFlow.Domain.Enums;

namespace DevFlow.Api.Contracts.Dashboard;

public record DashboardResponse(
    int ProjectCount,
    int TaskCount,
    int CompletedTaskCount,
    int OverdueTaskCount,
    IReadOnlyDictionary<TaskItemStatus, int> TasksByStatus,
    IReadOnlyDictionary<TaskPriority, int> TasksByPriority,
    IReadOnlyList<ActivityResponse> RecentActivity,
    IReadOnlyList<OverdueTaskResponse> OverdueTasks);

public record OverdueTaskResponse(
    Guid Id,
    string Title,
    Guid ProjectId,
    string ProjectName,
    DateOnly DueDate,
    TaskPriority Priority);
