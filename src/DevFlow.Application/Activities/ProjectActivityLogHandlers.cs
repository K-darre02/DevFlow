using DevFlow.Application.Common;
using DevFlow.Application.Realtime;
using DevFlow.Domain.Enums;
using MediatR;

namespace DevFlow.Application.Activities;

public class ProjectCreatedActivityLogHandler : INotificationHandler<ProjectCreatedNotification>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public ProjectCreatedActivityLogHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public Task Handle(ProjectCreatedNotification notification, CancellationToken cancellationToken)
    {
        var project = notification.Project;
        return ActivityLogWriter.WriteAsync(
            _context,
            project.TenantId,
            _currentUserService.UserId,
            ActivityType.ProjectCreated,
            ActivityEntityType.Project,
            project.Id,
            $"Project '{project.Name}' was created",
            metadata: null,
            cancellationToken);
    }
}

public class ProjectArchivedActivityLogHandler : INotificationHandler<ProjectArchivedNotification>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public ProjectArchivedActivityLogHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public Task Handle(ProjectArchivedNotification notification, CancellationToken cancellationToken)
    {
        var project = notification.Project;
        return ActivityLogWriter.WriteAsync(
            _context,
            project.TenantId,
            _currentUserService.UserId,
            ActivityType.ProjectArchived,
            ActivityEntityType.Project,
            project.Id,
            $"Project '{project.Name}' was archived",
            metadata: null,
            cancellationToken);
    }
}
