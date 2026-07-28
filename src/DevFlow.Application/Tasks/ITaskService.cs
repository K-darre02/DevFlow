using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;

namespace DevFlow.Application.Tasks;

public interface ITaskService
{
    Task<IReadOnlyList<TaskItem>> GetTasksAsync(
        Guid? projectId,
        TaskItemStatus? status,
        Guid? assigneeUserId,
        CancellationToken cancellationToken);

    Task<TaskItem?> GetTaskByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Throws NotFoundException if ProjectId/AssigneeUserId don't resolve within the current tenant.</summary>
    Task<TaskItem> CreateTaskAsync(Guid tenantId, CreateTaskInput input, CancellationToken cancellationToken);

    /// <summary>
    /// Returns null if the task doesn't exist (or isn't in the current tenant).
    /// Throws ConcurrencyConflictException&lt;TaskItem&gt; if expectedVersion
    /// doesn't match the task's current Version at save time.
    /// </summary>
    Task<TaskItem?> UpdateTaskAsync(Guid id, UpdateTaskInput input, int expectedVersion, CancellationToken cancellationToken);

    Task<bool> DeleteTaskAsync(Guid id, CancellationToken cancellationToken);
}
