using DevFlow.Domain.Enums;

namespace DevFlow.Api.Contracts.Notifications;

public record NotificationResponse(
    Guid Id,
    NotificationType Type,
    string Title,
    string Message,
    bool IsRead,
    DateTimeOffset CreatedAt);
