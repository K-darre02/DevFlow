using System.Text.Json;
using DevFlow.Application.Common;
using DevFlow.Application.Realtime;
using DevFlow.Domain.Enums;
using MediatR;

namespace DevFlow.Application.Activities;

// One handler per Task notification, each writing a single ActivityLog row.
// These run alongside DevFlow.Api/Realtime's SignalR handlers for the same
// notifications — see DependencyInjection's TaskWhenAllPublisher comment for
// why one failing doesn't block the other.
public class TaskCreatedActivityLogHandler : INotificationHandler<TaskCreatedNotification>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public TaskCreatedActivityLogHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public Task Handle(TaskCreatedNotification notification, CancellationToken cancellationToken)
    {
        var task = notification.Task;
        return ActivityLogWriter.WriteAsync(
            _context,
            task.TenantId,
            _currentUserService.UserId,
            ActivityType.TaskCreated,
            ActivityEntityType.Task,
            task.Id,
            $"Task '{task.Title}' was created",
            metadata: null,
            cancellationToken);
    }
}

public class TaskUpdatedActivityLogHandler : INotificationHandler<TaskUpdatedNotification>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public TaskUpdatedActivityLogHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public Task Handle(TaskUpdatedNotification notification, CancellationToken cancellationToken)
    {
        var task = notification.Task;
        return ActivityLogWriter.WriteAsync(
            _context,
            task.TenantId,
            _currentUserService.UserId,
            ActivityType.TaskUpdated,
            ActivityEntityType.Task,
            task.Id,
            $"Task '{task.Title}' was updated",
            metadata: null,
            cancellationToken);
    }
}

public class TaskMovedActivityLogHandler : INotificationHandler<TaskMovedNotification>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public TaskMovedActivityLogHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public Task Handle(TaskMovedNotification notification, CancellationToken cancellationToken)
    {
        var task = notification.Task;
        var metadata = JsonSerializer.Serialize(new { fromStatus = notification.FromStatus.ToString(), toStatus = task.Status.ToString() });
        return ActivityLogWriter.WriteAsync(
            _context,
            task.TenantId,
            _currentUserService.UserId,
            ActivityType.TaskMoved,
            ActivityEntityType.Task,
            task.Id,
            $"Task '{task.Title}' moved from {notification.FromStatus} to {task.Status}",
            metadata,
            cancellationToken);
    }
}

public class TaskAssignedActivityLogHandler : INotificationHandler<TaskAssignedNotification>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public TaskAssignedActivityLogHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public Task Handle(TaskAssignedNotification notification, CancellationToken cancellationToken)
    {
        var task = notification.Task;
        var metadata = JsonSerializer.Serialize(new { assigneeUserId = task.AssigneeUserId });
        return ActivityLogWriter.WriteAsync(
            _context,
            task.TenantId,
            _currentUserService.UserId,
            ActivityType.TaskAssigned,
            ActivityEntityType.Task,
            task.Id,
            $"Task '{task.Title}' was assigned",
            metadata,
            cancellationToken);
    }
}

public class TaskCompletedActivityLogHandler : INotificationHandler<TaskCompletedNotification>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public TaskCompletedActivityLogHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public Task Handle(TaskCompletedNotification notification, CancellationToken cancellationToken)
    {
        var task = notification.Task;
        return ActivityLogWriter.WriteAsync(
            _context,
            task.TenantId,
            _currentUserService.UserId,
            ActivityType.TaskCompleted,
            ActivityEntityType.Task,
            task.Id,
            $"Task '{task.Title}' was completed",
            metadata: null,
            cancellationToken);
    }
}

public class TaskDeletedActivityLogHandler : INotificationHandler<TaskDeletedNotification>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public TaskDeletedActivityLogHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public Task Handle(TaskDeletedNotification notification, CancellationToken cancellationToken)
    {
        var task = notification.Task;
        return ActivityLogWriter.WriteAsync(
            _context,
            task.TenantId,
            _currentUserService.UserId,
            ActivityType.TaskDeleted,
            ActivityEntityType.Task,
            task.Id,
            $"Task '{task.Title}' was deleted",
            metadata: null,
            cancellationToken);
    }
}
