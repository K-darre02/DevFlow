namespace DevFlow.Api.Contracts.Notifications;

// Shared by NotificationsController and the Realtime SignalR handler — see
// TaskResponseMapper for the same rationale.
public static class NotificationResponseMapper
{
    public static NotificationResponse ToResponse(Domain.Entities.Notification notification) => new(
        notification.Id,
        notification.Type,
        notification.Title,
        notification.Message,
        notification.IsRead,
        notification.CreatedAt);
}
