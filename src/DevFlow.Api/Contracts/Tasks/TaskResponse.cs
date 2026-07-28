using DevFlow.Domain.Enums;

namespace DevFlow.Api.Contracts.Tasks;

public record TaskResponse(
    Guid Id,
    Guid ProjectId,
    Guid? AssigneeUserId,
    string Title,
    string? Description,
    TaskItemStatus Status,
    TaskPriority Priority,
    DateOnly? DueDate,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    int Version);
