using DevFlow.Application.Common;
using DevFlow.Application.Realtime;
using DevFlow.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DevFlow.Application.Notifications;

// "Project changes" -> everyone in the tenant except whoever made the
// change, notified that the project was archived. Fan-out to every member
// (not just those with tasks in the project) is a deliberate simplification
// for this system's scale — a "who's actually involved with this project"
// concept doesn't otherwise exist yet.
public class ProjectArchivedNotificationTriggerHandler : INotificationHandler<ProjectArchivedNotification>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IPublisher _publisher;
    private readonly ILogger<ProjectArchivedNotificationTriggerHandler> _logger;

    public ProjectArchivedNotificationTriggerHandler(
        IApplicationDbContext context, ICurrentUserService currentUserService,
        IPublisher publisher, ILogger<ProjectArchivedNotificationTriggerHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task Handle(ProjectArchivedNotification notification, CancellationToken cancellationToken)
    {
        var project = notification.Project;

        var recipientUserIds = await _context.TenantMembers
            .Where(m => m.UserId != _currentUserService.UserId)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);

        foreach (var userId in recipientUserIds)
        {
            await NotificationWriter.WriteAsync(
                _context, _publisher, _logger,
                project.TenantId, userId,
                NotificationType.ProjectChanged,
                "Project archived",
                $"'{project.Name}' was archived.",
                cancellationToken);
        }
    }
}
