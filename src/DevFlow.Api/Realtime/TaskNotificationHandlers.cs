using DevFlow.Api.Contracts.Tasks;
using DevFlow.Application.Realtime;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace DevFlow.Api.Realtime;

// One handler per notification: each maps the domain entity to the same
// wire-format response the REST API returns, then broadcasts it to the
// tenant's group. MediatR resolves and invokes all of these per Publish
// call (TaskService.PublishSafeAsync already guarantees this only ever runs
// after a successful commit).

public class TaskCreatedNotificationHandler : INotificationHandler<TaskCreatedNotification>
{
    private readonly IHubContext<TaskHub> _hubContext;
    public TaskCreatedNotificationHandler(IHubContext<TaskHub> hubContext) => _hubContext = hubContext;

    public Task Handle(TaskCreatedNotification notification, CancellationToken cancellationToken) =>
        _hubContext.Clients.Group(TaskHub.GroupName(notification.Task.TenantId))
            .SendAsync("task.created", TaskResponseMapper.ToResponse(notification.Task), cancellationToken);
}

public class TaskUpdatedNotificationHandler : INotificationHandler<TaskUpdatedNotification>
{
    private readonly IHubContext<TaskHub> _hubContext;
    public TaskUpdatedNotificationHandler(IHubContext<TaskHub> hubContext) => _hubContext = hubContext;

    public Task Handle(TaskUpdatedNotification notification, CancellationToken cancellationToken) =>
        _hubContext.Clients.Group(TaskHub.GroupName(notification.Task.TenantId))
            .SendAsync("task.updated", TaskResponseMapper.ToResponse(notification.Task), cancellationToken);
}

public class TaskMovedNotificationHandler : INotificationHandler<TaskMovedNotification>
{
    private readonly IHubContext<TaskHub> _hubContext;
    public TaskMovedNotificationHandler(IHubContext<TaskHub> hubContext) => _hubContext = hubContext;

    public Task Handle(TaskMovedNotification notification, CancellationToken cancellationToken) =>
        _hubContext.Clients.Group(TaskHub.GroupName(notification.Task.TenantId))
            .SendAsync(
                "task.moved",
                TaskResponseMapper.ToResponse(notification.Task),
                notification.FromStatus,
                cancellationToken);
}

public class TaskAssignedNotificationHandler : INotificationHandler<TaskAssignedNotification>
{
    private readonly IHubContext<TaskHub> _hubContext;
    public TaskAssignedNotificationHandler(IHubContext<TaskHub> hubContext) => _hubContext = hubContext;

    public Task Handle(TaskAssignedNotification notification, CancellationToken cancellationToken) =>
        _hubContext.Clients.Group(TaskHub.GroupName(notification.Task.TenantId))
            .SendAsync("task.assigned", TaskResponseMapper.ToResponse(notification.Task), cancellationToken);
}

public class TaskCompletedNotificationHandler : INotificationHandler<TaskCompletedNotification>
{
    private readonly IHubContext<TaskHub> _hubContext;
    public TaskCompletedNotificationHandler(IHubContext<TaskHub> hubContext) => _hubContext = hubContext;

    public Task Handle(TaskCompletedNotification notification, CancellationToken cancellationToken) =>
        _hubContext.Clients.Group(TaskHub.GroupName(notification.Task.TenantId))
            .SendAsync("task.completed", TaskResponseMapper.ToResponse(notification.Task), cancellationToken);
}

public class TaskDeletedNotificationHandler : INotificationHandler<TaskDeletedNotification>
{
    private readonly IHubContext<TaskHub> _hubContext;
    public TaskDeletedNotificationHandler(IHubContext<TaskHub> hubContext) => _hubContext = hubContext;

    public Task Handle(TaskDeletedNotification notification, CancellationToken cancellationToken) =>
        _hubContext.Clients.Group(TaskHub.GroupName(notification.Task.TenantId))
            .SendAsync("task.deleted", notification.Task.Id, notification.Task.ProjectId, cancellationToken);
}
