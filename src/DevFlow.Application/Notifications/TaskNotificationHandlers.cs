using DevFlow.Application.Common;
using DevFlow.Application.Realtime;
using DevFlow.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace DevFlow.Application.Notifications;

public class TaskAssignedNotificationTriggerHandler : INotificationHandler<TaskAssignedNotification>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IPublisher _publisher;
    private readonly ILogger<TaskAssignedNotificationTriggerHandler> _logger;

    public TaskAssignedNotificationTriggerHandler(
        IApplicationDbContext context, ICurrentUserService currentUserService,
        IPublisher publisher, ILogger<TaskAssignedNotificationTriggerHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task Handle(TaskAssignedNotification notification, CancellationToken cancellationToken)
    {
        var task = notification.Task;

        if (task.AssigneeUserId is null || task.AssigneeUserId == _currentUserService.UserId)
        {
            // No assignee (shouldn't happen — this notification only fires
            // when AssigneeUserId changed to a non-null value), or the actor
            // assigned the task to themselves — nothing to tell them.
            return;
        }

        await NotificationWriter.WriteAsync(
            _context, _publisher, _logger,
            task.TenantId, task.AssigneeUserId.Value,
            NotificationType.TaskAssigned,
            "Task assigned to you",
            $"You were assigned '{task.Title}'.",
            cancellationToken);
    }
}

public class TaskMentionedNotificationTriggerHandler : INotificationHandler<TaskMentionedNotification>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IPublisher _publisher;
    private readonly ILogger<TaskMentionedNotificationTriggerHandler> _logger;

    public TaskMentionedNotificationTriggerHandler(
        IApplicationDbContext context, ICurrentUserService currentUserService,
        IPublisher publisher, ILogger<TaskMentionedNotificationTriggerHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task Handle(TaskMentionedNotification notification, CancellationToken cancellationToken)
    {
        var task = notification.Task;

        foreach (var mentionedUserId in notification.MentionedUserIds)
        {
            if (mentionedUserId == _currentUserService.UserId)
            {
                continue; // don't notify someone for mentioning themselves
            }

            await NotificationWriter.WriteAsync(
                _context, _publisher, _logger,
                task.TenantId, mentionedUserId,
                NotificationType.TaskMentioned,
                "You were mentioned",
                $"You were mentioned in '{task.Title}'.",
                cancellationToken);
        }
    }
}
