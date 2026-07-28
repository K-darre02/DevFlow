using DevFlow.Domain.Enums;

namespace DevFlow.Api.Contracts.Activity;

public record ActivityResponse(
    Guid Id,
    ActivityType ActivityType,
    ActivityEntityType EntityType,
    Guid EntityId,
    string Description,
    string? Metadata,
    Guid? UserId,
    /// <summary>Resolved via a join at read time — null if UserId is null.</summary>
    string? UserEmail,
    DateTimeOffset CreatedAt);
