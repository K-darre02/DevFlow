using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using MediatR;

namespace DevFlow.Application.Realtime;

// Same post-commit, published-only-after-a-successful-write contract as
// Task/ProjectNotifications — published by TeamService.PublishSafeAsync.
// Unlike the Task/Project notifications, nothing in Api/Realtime subscribes
// to these (Team events were never part of the SignalR broadcast list) —
// today their only consumer is Application/ActivityLog's notification
// handlers, which is exactly the "independent side effects fanning out from
// one Publish call" this mechanism exists for.

public record MemberInvitedNotification(Invitation Invitation) : INotification;

public record MemberJoinedNotification(TenantMember Membership) : INotification;

public record RoleChangedNotification(TenantMember Membership, TenantRole PreviousRole) : INotification;

public record MemberRemovedNotification(TenantMember Membership) : INotification;
