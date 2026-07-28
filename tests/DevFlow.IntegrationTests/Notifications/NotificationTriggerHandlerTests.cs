using DevFlow.Application.Notifications;
using DevFlow.Application.Realtime;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using DevFlow.IntegrationTests.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevFlow.IntegrationTests.Notifications;

// Direct handler tests, same rationale as ActivityLogNotificationHandlerTests:
// fast, pinpoint exactly which trigger is under test. The self-notification
// suppression guards (don't notify someone about their own action) are the
// main thing worth covering here beyond "does a row get created."
public class NotificationTriggerHandlerTests : SqliteContextFixture
{
    private readonly Tenant _tenant;
    private readonly User _actor;
    private readonly RecordingPublisher _publisher = new();

    public NotificationTriggerHandlerTests()
    {
        _tenant = new Tenant { Name = "Tenant" };
        _actor = new User { Email = "actor@example.com", PasswordHash = "unused" };
        DbContext.AddRange(_tenant, _actor);
        DbContext.SaveChangesAsync().GetAwaiter().GetResult();

        CurrentUser.TenantId = _tenant.Id;
        CurrentUser.UserId = _actor.Id;
    }

    private async Task<Notification> OnlyNotificationAsync()
    {
        var notifications = await DbContext.Notifications.IgnoreQueryFilters().ToListAsync();
        return notifications.Should().ContainSingle().Subject;
    }

    [Fact]
    public async Task TaskAssigned_notifies_the_assignee()
    {
        var handler = new TaskAssignedNotificationTriggerHandler(DbContext, CurrentUser, _publisher, NullLogger<TaskAssignedNotificationTriggerHandler>.Instance);
        var assignee = new User { Email = "assignee@example.com", PasswordHash = "unused" };
        DbContext.Users.Add(assignee);
        await DbContext.SaveChangesAsync();
        var task = new TaskItem { TenantId = _tenant.Id, Tenant = _tenant, Title = "Fix bug", AssigneeUserId = assignee.Id };

        await handler.Handle(new TaskAssignedNotification(task), default);

        var notification = await OnlyNotificationAsync();
        notification.UserId.Should().Be(assignee.Id);
        notification.Type.Should().Be(NotificationType.TaskAssigned);
        notification.Message.Should().Contain("Fix bug");
    }

    [Fact]
    public async Task TaskAssigned_does_not_notify_when_you_assign_a_task_to_yourself()
    {
        var handler = new TaskAssignedNotificationTriggerHandler(DbContext, CurrentUser, _publisher, NullLogger<TaskAssignedNotificationTriggerHandler>.Instance);
        var task = new TaskItem { TenantId = _tenant.Id, Tenant = _tenant, Title = "Self-assigned", AssigneeUserId = _actor.Id };

        await handler.Handle(new TaskAssignedNotification(task), default);

        (await DbContext.Notifications.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TaskMentioned_notifies_each_mentioned_user()
    {
        var handler = new TaskMentionedNotificationTriggerHandler(DbContext, CurrentUser, _publisher, NullLogger<TaskMentionedNotificationTriggerHandler>.Instance);
        var mentioned = new User { Email = "mentioned@example.com", PasswordHash = "unused" };
        DbContext.Users.Add(mentioned);
        await DbContext.SaveChangesAsync();
        var task = new TaskItem { TenantId = _tenant.Id, Tenant = _tenant, Title = "Needs review" };

        await handler.Handle(new TaskMentionedNotification(task, new[] { mentioned.Id }), default);

        var notification = await OnlyNotificationAsync();
        notification.UserId.Should().Be(mentioned.Id);
        notification.Type.Should().Be(NotificationType.TaskMentioned);
    }

    [Fact]
    public async Task TaskMentioned_does_not_notify_someone_for_mentioning_themselves()
    {
        var handler = new TaskMentionedNotificationTriggerHandler(DbContext, CurrentUser, _publisher, NullLogger<TaskMentionedNotificationTriggerHandler>.Instance);
        var task = new TaskItem { TenantId = _tenant.Id, Tenant = _tenant, Title = "Self mention" };

        await handler.Handle(new TaskMentionedNotification(task, new[] { _actor.Id }), default);

        (await DbContext.Notifications.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProjectArchived_notifies_every_other_tenant_member_but_not_the_actor()
    {
        var handler = new ProjectArchivedNotificationTriggerHandler(DbContext, CurrentUser, _publisher, NullLogger<ProjectArchivedNotificationTriggerHandler>.Instance);
        var otherMemberUser = new User { Email = "member@example.com", PasswordHash = "unused" };
        var actorMembership = new TenantMember { TenantId = _tenant.Id, Tenant = _tenant, UserId = _actor.Id, User = _actor, Role = TenantRole.Owner };
        var otherMembership = new TenantMember { TenantId = _tenant.Id, Tenant = _tenant, UserId = otherMemberUser.Id, User = otherMemberUser, Role = TenantRole.Member };
        DbContext.AddRange(otherMemberUser, actorMembership, otherMembership);
        await DbContext.SaveChangesAsync();
        var project = new Project { TenantId = _tenant.Id, Tenant = _tenant, Name = "Retired Project", IsArchived = true };

        await handler.Handle(new ProjectArchivedNotification(project), default);

        var notification = await OnlyNotificationAsync();
        notification.UserId.Should().Be(otherMemberUser.Id);
        notification.Type.Should().Be(NotificationType.ProjectChanged);
    }

    [Fact]
    public async Task MemberJoined_notifies_the_inviter()
    {
        var handler = new MemberJoinedNotificationTriggerHandler(DbContext, _publisher, NullLogger<MemberJoinedNotificationTriggerHandler>.Instance);
        var inviter = new User { Email = "inviter@example.com", PasswordHash = "unused" };
        var joiner = new User { Email = "joiner@example.com", PasswordHash = "unused" };
        DbContext.AddRange(inviter, joiner);
        await DbContext.SaveChangesAsync();
        var membership = new TenantMember { TenantId = _tenant.Id, Tenant = _tenant, UserId = joiner.Id, User = joiner, Role = TenantRole.Member };

        await handler.Handle(new MemberJoinedNotification(membership, inviter.Id), default);

        var notification = await OnlyNotificationAsync();
        notification.UserId.Should().Be(inviter.Id);
        notification.Type.Should().Be(NotificationType.TeamInvitationAccepted);
        notification.Message.Should().Contain("joiner@example.com");
    }

    [Fact]
    public async Task RoleChanged_notifies_the_member_whose_role_changed()
    {
        var handler = new RoleChangedNotificationTriggerHandler(DbContext, CurrentUser, _publisher, NullLogger<RoleChangedNotificationTriggerHandler>.Instance);
        var member = new User { Email = "member@example.com", PasswordHash = "unused" };
        DbContext.Users.Add(member);
        await DbContext.SaveChangesAsync();
        var membership = new TenantMember { TenantId = _tenant.Id, Tenant = _tenant, UserId = member.Id, User = member, Role = TenantRole.Admin };

        await handler.Handle(new RoleChangedNotification(membership, TenantRole.Member), default);

        var notification = await OnlyNotificationAsync();
        notification.UserId.Should().Be(member.Id);
        notification.Type.Should().Be(NotificationType.RoleChanged);
        notification.Message.Should().Contain("Member").And.Contain("Admin");
    }

    [Fact]
    public async Task RoleChanged_does_not_notify_yourself_when_you_change_your_own_role()
    {
        var handler = new RoleChangedNotificationTriggerHandler(DbContext, CurrentUser, _publisher, NullLogger<RoleChangedNotificationTriggerHandler>.Instance);
        var membership = new TenantMember { TenantId = _tenant.Id, Tenant = _tenant, UserId = _actor.Id, User = _actor, Role = TenantRole.Owner };

        await handler.Handle(new RoleChangedNotification(membership, TenantRole.Admin), default);

        (await DbContext.Notifications.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }
}
