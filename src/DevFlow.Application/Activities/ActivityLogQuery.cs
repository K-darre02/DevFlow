using DevFlow.Domain.Enums;

namespace DevFlow.Application.Activities;

public record ActivityLogQuery(
    int Page,
    int PageSize,
    ActivityEntityType? EntityType,
    Guid? UserId,
    ActivityType? ActivityType);
