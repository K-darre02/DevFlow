using DevFlow.Application.Common;
using DevFlow.Application.Realtime;
using DevFlow.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace DevFlow.Application.Notifications;

public class MemberJoinedNotificationTriggerHandler : INotificationHandler<MemberJoinedNotification>
{
    private readonly IApplicationDbContext _context;
    private readonly IPublisher _publisher;
    private readonly ILogger<MemberJoinedNotificationTriggerHandler> _logger;

    public MemberJoinedNotificationTriggerHandler(
        IApplicationDbContext context, IPublisher publisher, ILogger<MemberJoinedNotificationTriggerHandler> logger)
    {
        _context = context;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task Handle(MemberJoinedNotification notification, CancellationToken cancellationToken)
    {
        var membership = notification.Membership;

        if (notification.InvitedByUserId == membership.UserId)
        {
            return; // self-invited (shouldn't normally happen) — no one to tell
        }

        await NotificationWriter.WriteAsync(
            _context, _publisher, _logger,
            membership.TenantId, notification.InvitedByUserId,
            NotificationType.TeamInvitationAccepted,
            "Invitation accepted",
            $"{membership.User.Email} accepted your invitation and joined the workspace.",
            cancellationToken);
    }
}

public class RoleChangedNotificationTriggerHandler : INotificationHandler<RoleChangedNotification>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IPublisher _publisher;
    private readonly ILogger<RoleChangedNotificationTriggerHandler> _logger;

    public RoleChangedNotificationTriggerHandler(
        IApplicationDbContext context, ICurrentUserService currentUserService,
        IPublisher publisher, ILogger<RoleChangedNotificationTriggerHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task Handle(RoleChangedNotification notification, CancellationToken cancellationToken)
    {
        var membership = notification.Membership;

        if (membership.UserId == _currentUserService.UserId)
        {
            return; // changed their own role — nothing to notify themselves about
        }

        await NotificationWriter.WriteAsync(
            _context, _publisher, _logger,
            membership.TenantId, membership.UserId,
            NotificationType.RoleChanged,
            "Your role changed",
            $"Your role changed from {notification.PreviousRole} to {membership.Role}.",
            cancellationToken);
    }
}
