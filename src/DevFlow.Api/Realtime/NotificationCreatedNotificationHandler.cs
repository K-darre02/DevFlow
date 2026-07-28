using DevFlow.Api.Contracts.Notifications;
using DevFlow.Application.Notifications;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace DevFlow.Api.Realtime;

// The one Realtime handler that broadcasts to a per-user group
// (TaskHub.UserGroupName) instead of the tenant-wide group every other
// handler in this file uses — a notification is only ever for the one
// person it was created for.
public class NotificationCreatedNotificationHandler : INotificationHandler<NotificationCreatedNotification>
{
    private readonly IHubContext<TaskHub> _hubContext;
    public NotificationCreatedNotificationHandler(IHubContext<TaskHub> hubContext) => _hubContext = hubContext;

    public Task Handle(NotificationCreatedNotification notification, CancellationToken cancellationToken) =>
        _hubContext.Clients.Group(TaskHub.UserGroupName(notification.Notification.UserId))
            .SendAsync("notification.created", NotificationResponseMapper.ToResponse(notification.Notification), cancellationToken);
}
