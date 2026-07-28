using DevFlow.Api.Contracts.Projects;
using DevFlow.Application.Realtime;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace DevFlow.Api.Realtime;

public class ProjectCreatedNotificationHandler : INotificationHandler<ProjectCreatedNotification>
{
    private readonly IHubContext<TaskHub> _hubContext;
    public ProjectCreatedNotificationHandler(IHubContext<TaskHub> hubContext) => _hubContext = hubContext;

    public Task Handle(ProjectCreatedNotification notification, CancellationToken cancellationToken) =>
        _hubContext.Clients.Group(TaskHub.GroupName(notification.Project.TenantId))
            .SendAsync("project.created", ProjectResponseMapper.ToResponse(notification.Project), cancellationToken);
}

public class ProjectArchivedNotificationHandler : INotificationHandler<ProjectArchivedNotification>
{
    private readonly IHubContext<TaskHub> _hubContext;
    public ProjectArchivedNotificationHandler(IHubContext<TaskHub> hubContext) => _hubContext = hubContext;

    public Task Handle(ProjectArchivedNotification notification, CancellationToken cancellationToken) =>
        _hubContext.Clients.Group(TaskHub.GroupName(notification.Project.TenantId))
            .SendAsync("project.archived", ProjectResponseMapper.ToResponse(notification.Project), cancellationToken);
}
