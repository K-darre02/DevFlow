using DevFlow.Application.Activities;
using DevFlow.Application.Realtime;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using DevFlow.IntegrationTests.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevFlow.IntegrationTests.Activities;

// Direct handler tests: each ActivityLog notification handler is
// instantiated and invoked directly (bypassing MediatR's Publish) so these
// stay fast and pinpoint exactly which handler is under test. The separate
// TaskWritesActivityLogEndToEndTests class proves the full wiring (real
// TaskService/ProjectService/TeamService call -> real MediatR Publish ->
// this handler actually runs) for a representative few — this file is the
// exhaustive, one-per-activity-type coverage.
public class ActivityLogNotificationHandlerTests : SqliteContextFixture
{
    private readonly Tenant _tenant;
    private readonly User _actor;
    private readonly Project _project;

    public ActivityLogNotificationHandlerTests()
    {
        _tenant = new Tenant { Name = "Tenant" };
        _actor = new User { Email = "actor@example.com", PasswordHash = "unused" };
        _project = new Project { TenantId = _tenant.Id, Tenant = _tenant, Name = "Project" };
        DbContext.AddRange(_tenant, _actor, _project);
        DbContext.SaveChangesAsync().GetAwaiter().GetResult();

        CurrentUser.TenantId = _tenant.Id;
        CurrentUser.UserId = _actor.Id;
    }

    private TaskItem NewTask(string title = "Fix login bug") =>
        new() { TenantId = _tenant.Id, Tenant = _tenant, ProjectId = _project.Id, Project = _project, Title = title };

    private async Task<ActivityLog> OnlyActivityAsync()
    {
        var activities = await DbContext.ActivityLogs.ToListAsync();
        return activities.Should().ContainSingle().Subject;
    }

    [Fact]
    public async Task TaskCreated_writes_an_activity_log_row()
    {
        var handler = new TaskCreatedActivityLogHandler(DbContext, CurrentUser);
        var task = NewTask("Write tests");

        await handler.Handle(new TaskCreatedNotification(task), default);

        var activity = await OnlyActivityAsync();
        activity.TenantId.Should().Be(_tenant.Id);
        activity.UserId.Should().Be(_actor.Id);
        activity.ActivityType.Should().Be(ActivityType.TaskCreated);
        activity.EntityType.Should().Be(ActivityEntityType.Task);
        activity.EntityId.Should().Be(task.Id);
        activity.Description.Should().Contain("Write tests");
    }

    [Fact]
    public async Task TaskUpdated_writes_an_activity_log_row()
    {
        var handler = new TaskUpdatedActivityLogHandler(DbContext, CurrentUser);
        var task = NewTask();

        await handler.Handle(new TaskUpdatedNotification(task), default);

        var activity = await OnlyActivityAsync();
        activity.ActivityType.Should().Be(ActivityType.TaskUpdated);
        activity.EntityType.Should().Be(ActivityEntityType.Task);
    }

    [Fact]
    public async Task TaskMoved_writes_an_activity_log_row_with_from_and_to_status_in_the_description()
    {
        var handler = new TaskMovedActivityLogHandler(DbContext, CurrentUser);
        var task = NewTask();
        task.Status = TaskItemStatus.InProgress;

        await handler.Handle(new TaskMovedNotification(task, TaskItemStatus.ToDo), default);

        var activity = await OnlyActivityAsync();
        activity.ActivityType.Should().Be(ActivityType.TaskMoved);
        activity.Description.Should().Contain("ToDo").And.Contain("InProgress");
        activity.Metadata.Should().Contain("ToDo").And.Contain("InProgress");
    }

    [Fact]
    public async Task TaskAssigned_writes_an_activity_log_row()
    {
        var handler = new TaskAssignedActivityLogHandler(DbContext, CurrentUser);
        var task = NewTask();
        task.AssigneeUserId = _actor.Id;

        await handler.Handle(new TaskAssignedNotification(task), default);

        var activity = await OnlyActivityAsync();
        activity.ActivityType.Should().Be(ActivityType.TaskAssigned);
        activity.Metadata.Should().Contain(_actor.Id.ToString());
    }

    [Fact]
    public async Task TaskCompleted_writes_an_activity_log_row()
    {
        var handler = new TaskCompletedActivityLogHandler(DbContext, CurrentUser);
        var task = NewTask();

        await handler.Handle(new TaskCompletedNotification(task), default);

        var activity = await OnlyActivityAsync();
        activity.ActivityType.Should().Be(ActivityType.TaskCompleted);
    }

    [Fact]
    public async Task TaskDeleted_writes_an_activity_log_row()
    {
        var handler = new TaskDeletedActivityLogHandler(DbContext, CurrentUser);
        var task = NewTask("Doomed task");

        await handler.Handle(new TaskDeletedNotification(task), default);

        var activity = await OnlyActivityAsync();
        activity.ActivityType.Should().Be(ActivityType.TaskDeleted);
        activity.Description.Should().Contain("Doomed task");
    }

    [Fact]
    public async Task ProjectCreated_writes_an_activity_log_row()
    {
        var handler = new ProjectCreatedActivityLogHandler(DbContext, CurrentUser);

        await handler.Handle(new ProjectCreatedNotification(_project), default);

        var activity = await OnlyActivityAsync();
        activity.ActivityType.Should().Be(ActivityType.ProjectCreated);
        activity.EntityType.Should().Be(ActivityEntityType.Project);
        activity.EntityId.Should().Be(_project.Id);
        activity.Description.Should().Contain(_project.Name);
    }

    [Fact]
    public async Task ProjectArchived_writes_an_activity_log_row()
    {
        var handler = new ProjectArchivedActivityLogHandler(DbContext, CurrentUser);

        await handler.Handle(new ProjectArchivedNotification(_project), default);

        var activity = await OnlyActivityAsync();
        activity.ActivityType.Should().Be(ActivityType.ProjectArchived);
    }

    [Fact]
    public async Task MemberInvited_writes_an_activity_log_row_attributed_to_the_inviter_not_the_current_user()
    {
        var handler = new MemberInvitedActivityLogHandler(DbContext);
        // A real, seeded User (ActivityLog.UserId has a foreign key to
        // Users) — deliberately different from CurrentUser.UserId (_actor),
        // to prove attribution comes from the invitation's own
        // InvitedByUserId, not ambient current-user state.
        var inviter = new User { Email = "inviter@example.com", PasswordHash = "unused" };
        DbContext.Users.Add(inviter);
        await DbContext.SaveChangesAsync();

        var invitation = new Invitation
        {
            TenantId = _tenant.Id,
            Tenant = _tenant,
            Email = "invitee@example.com",
            Role = TenantRole.Member,
            TokenHash = "hash",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            InvitedByUserId = inviter.Id
        };

        await handler.Handle(new MemberInvitedNotification(invitation), default);

        var activity = await OnlyActivityAsync();
        activity.ActivityType.Should().Be(ActivityType.MemberInvited);
        activity.EntityType.Should().Be(ActivityEntityType.Invitation);
        activity.EntityId.Should().Be(invitation.Id);
        activity.UserId.Should().Be(inviter.Id);
        activity.Description.Should().Contain("invitee@example.com");
    }

    [Fact]
    public async Task MemberJoined_writes_an_activity_log_row_attributed_to_the_joining_user()
    {
        var handler = new MemberJoinedActivityLogHandler(DbContext);
        var joiningUser = new User { Email = "newmember@example.com", PasswordHash = "unused" };
        DbContext.Users.Add(joiningUser);
        await DbContext.SaveChangesAsync();
        var membership = new TenantMember { TenantId = _tenant.Id, Tenant = _tenant, UserId = joiningUser.Id, User = joiningUser, Role = TenantRole.Member };

        await handler.Handle(new MemberJoinedNotification(membership, _actor.Id), default);

        var activity = await OnlyActivityAsync();
        activity.ActivityType.Should().Be(ActivityType.MemberJoined);
        activity.EntityType.Should().Be(ActivityEntityType.TeamMember);
        activity.UserId.Should().Be(joiningUser.Id);
        activity.Description.Should().Contain("newmember@example.com");
    }

    [Fact]
    public async Task RoleChanged_writes_an_activity_log_row_with_previous_and_new_role()
    {
        var handler = new RoleChangedActivityLogHandler(DbContext, CurrentUser);
        var member = new TenantMember { TenantId = _tenant.Id, Tenant = _tenant, UserId = _actor.Id, User = _actor, Role = TenantRole.Admin };

        await handler.Handle(new RoleChangedNotification(member, TenantRole.Member), default);

        var activity = await OnlyActivityAsync();
        activity.ActivityType.Should().Be(ActivityType.RoleChanged);
        activity.Description.Should().Contain("Member").And.Contain("Admin");
        activity.Metadata.Should().Contain("Member").And.Contain("Admin");
    }

    [Fact]
    public async Task MemberRemoved_writes_an_activity_log_row()
    {
        var handler = new MemberRemovedActivityLogHandler(DbContext, CurrentUser);
        var removedUser = new User { Email = "gone@example.com", PasswordHash = "unused" };
        DbContext.Users.Add(removedUser);
        await DbContext.SaveChangesAsync();
        var member = new TenantMember { TenantId = _tenant.Id, Tenant = _tenant, UserId = removedUser.Id, User = removedUser, Role = TenantRole.Member };

        await handler.Handle(new MemberRemovedNotification(member), default);

        var activity = await OnlyActivityAsync();
        activity.ActivityType.Should().Be(ActivityType.MemberRemoved);
        activity.EntityType.Should().Be(ActivityEntityType.TeamMember);
        activity.Description.Should().Contain("gone@example.com");
    }
}
