using DevFlow.Domain.Entities;

namespace DevFlow.Api.Contracts.Tasks;

// Shared by TasksController and the Realtime notification handlers
// (DevFlow.Api/Realtime) — one place that knows how a TaskItem becomes wire
// format, rather than the HTTP response and the SignalR broadcast quietly
// drifting apart.
public static class TaskResponseMapper
{
    public static TaskResponse ToResponse(TaskItem task) => new(
        task.Id,
        task.ProjectId,
        task.AssigneeUserId,
        task.Title,
        task.Description,
        task.Status,
        task.Priority,
        task.DueDate,
        task.CompletedAt,
        task.CreatedAt,
        task.Version);
}
