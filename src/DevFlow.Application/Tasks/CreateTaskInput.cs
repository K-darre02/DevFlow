using DevFlow.Domain.Enums;

namespace DevFlow.Application.Tasks;

public record CreateTaskInput(
    Guid ProjectId,
    string Title,
    string? Description,
    TaskPriority Priority,
    Guid? AssigneeUserId,
    DateOnly? DueDate);
