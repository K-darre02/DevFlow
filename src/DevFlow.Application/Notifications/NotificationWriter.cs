using DevFlow.Application.Common;
using DevFlow.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace DevFlow.Application.Notifications;

// Shared by every notification-trigger handler (Task/Project/Team) — same
// role as Activities.ActivityLogWriter: "create the row, save, publish the
// follow-on event" in one place instead of six times. Unlike
// ActivityLogWriter, this one owns its own safe-publish (try/catch + log)
// rather than leaving that to the caller: every one of this writer's
// callers is itself already inside a notification handler invoked via
// TaskWhenAllPublisher, not a request-handling service method — there's no
// single "PublishSafeAsync" home for it to delegate back to the way
// TaskService/ProjectService/TeamService have their own.
internal static class NotificationWriter
{
    public static async Task WriteAsync(
        IApplicationDbContext context,
        IPublisher publisher,
        ILogger logger,
        Guid tenantId,
        Guid userId,
        NotificationType type,
        string title,
        string message,
        CancellationToken cancellationToken)
    {
        var notification = new Domain.Entities.Notification
        {
            TenantId = tenantId,
            UserId = userId,
            Type = type,
            Title = title,
            Message = message
        };

        context.Notifications.Add(notification);
        await context.SaveChangesAsync(cancellationToken);

        try
        {
            await publisher.Publish(new NotificationCreatedNotification(notification), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish NotificationCreatedNotification after creating a {NotificationType} notification", type);
        }
    }
}
