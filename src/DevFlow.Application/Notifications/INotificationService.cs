using DevFlow.Application.Common;
using DevFlow.Domain.Entities;

namespace DevFlow.Application.Notifications;

public interface INotificationService
{
    /// <summary>Newest first, scoped to the current tenant *and* user via the ambient query filter. Page is 1-based.</summary>
    Task<PagedResult<Notification>> GetNotificationsAsync(NotificationQuery query, CancellationToken cancellationToken);

    Task<int> GetUnreadCountAsync(CancellationToken cancellationToken);

    /// <summary>Null if the notification doesn't exist for the current user (the query filter makes another user's/tenant's notification indistinguishable from nonexistent).</summary>
    Task<Notification?> MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken);

    /// <summary>Returns how many were newly marked read.</summary>
    Task<int> MarkAllAsReadAsync(CancellationToken cancellationToken);
}
