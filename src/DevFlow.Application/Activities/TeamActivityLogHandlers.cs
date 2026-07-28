using System.Text.Json;
using DevFlow.Application.Common;
using DevFlow.Application.Realtime;
using DevFlow.Domain.Enums;
using MediatR;

namespace DevFlow.Application.Activities;

// Unlike the Task/Project handlers, these don't use ICurrentUserService for
// the actor: MemberInvited's actor is the invitation's own InvitedByUserId
// (explicit domain data, no reason to prefer ambient state), and
// MemberJoined happens through an anonymous endpoint (AcceptInvitation) —
// there is no authenticated current user at that point, only the person who
// just joined, which is exactly who the activity should be attributed to.
public class MemberInvitedActivityLogHandler : INotificationHandler<MemberInvitedNotification>
{
    private readonly IApplicationDbContext _context;

    public MemberInvitedActivityLogHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public Task Handle(MemberInvitedNotification notification, CancellationToken cancellationToken)
    {
        var invitation = notification.Invitation;
        return ActivityLogWriter.WriteAsync(
            _context,
            invitation.TenantId,
            invitation.InvitedByUserId,
            ActivityType.MemberInvited,
            ActivityEntityType.Invitation,
            invitation.Id,
            $"{invitation.Email} was invited to join as {invitation.Role}",
            metadata: null,
            cancellationToken);
    }
}

public class MemberJoinedActivityLogHandler : INotificationHandler<MemberJoinedNotification>
{
    private readonly IApplicationDbContext _context;

    public MemberJoinedActivityLogHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public Task Handle(MemberJoinedNotification notification, CancellationToken cancellationToken)
    {
        var membership = notification.Membership;
        return ActivityLogWriter.WriteAsync(
            _context,
            membership.TenantId,
            membership.UserId,
            ActivityType.MemberJoined,
            ActivityEntityType.TeamMember,
            membership.Id,
            $"{membership.User.Email} joined the workspace",
            metadata: null,
            cancellationToken);
    }
}

public class RoleChangedActivityLogHandler : INotificationHandler<RoleChangedNotification>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public RoleChangedActivityLogHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public Task Handle(RoleChangedNotification notification, CancellationToken cancellationToken)
    {
        var membership = notification.Membership;
        var metadata = JsonSerializer.Serialize(new
        {
            previousRole = notification.PreviousRole.ToString(),
            newRole = membership.Role.ToString()
        });

        return ActivityLogWriter.WriteAsync(
            _context,
            membership.TenantId,
            _currentUserService.UserId,
            ActivityType.RoleChanged,
            ActivityEntityType.TeamMember,
            membership.Id,
            $"{membership.User.Email}'s role changed from {notification.PreviousRole} to {membership.Role}",
            metadata,
            cancellationToken);
    }
}

public class MemberRemovedActivityLogHandler : INotificationHandler<MemberRemovedNotification>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public MemberRemovedActivityLogHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public Task Handle(MemberRemovedNotification notification, CancellationToken cancellationToken)
    {
        var membership = notification.Membership;
        return ActivityLogWriter.WriteAsync(
            _context,
            membership.TenantId,
            _currentUserService.UserId,
            ActivityType.MemberRemoved,
            ActivityEntityType.TeamMember,
            membership.Id,
            $"{membership.User.Email} was removed from the workspace",
            metadata: null,
            cancellationToken);
    }
}
