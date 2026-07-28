using DevFlow.Application;
using DevFlow.Application.Common;
using DevFlow.Application.Projects;
using DevFlow.Application.Tasks;
using DevFlow.Application.Team;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using DevFlow.IntegrationTests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DevFlow.IntegrationTests.Activities;

// Proves the actual wiring, not just each handler in isolation: a real
// ServiceCollection built the same way AddApplicationServices assembles it
// (real MediatR, real TaskWhenAllPublisher, real assembly-scanned
// INotificationHandler<T> registrations), driven through the same
// TaskService/ProjectService/TeamService methods the controllers call. If
// DI wiring for the Activity handlers were ever wrong (wrong assembly,
// wrong lifetime, forgotten registration), this is what would catch it —
// ActivityLogNotificationHandlerTests calling .Handle(...) directly
// wouldn't.
public class ActivityLogEndToEndTests : SqliteContextFixture
{
    private readonly List<string> CapturedLogs = new();
    private readonly ServiceProvider _provider;
    private readonly Tenant _tenant;

    public ActivityLogEndToEndTests()
    {
        _tenant = new Tenant { Name = "Tenant" };
        var currentUser = new User { Email = "current@example.com", PasswordHash = "unused" };
        DbContext.AddRange(_tenant, currentUser);
        DbContext.SaveChangesAsync().GetAwaiter().GetResult();
        CurrentUser.TenantId = _tenant.Id;
        // A real, seeded User — ActivityLog.UserId has a foreign key to
        // Users, so a random unseeded Guid here would violate it the moment
        // an ActivityLog handler tries to attribute an activity to it.
        CurrentUser.UserId = currentUser.Id;

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(new CapturingLoggerProvider(CapturedLogs)));
        services.AddApplicationServices();
        services.AddSingleton<IApplicationDbContext>(DbContext);
        services.AddSingleton<ICurrentUserService>(CurrentUser);
        services.AddSingleton<IPasswordHasher<User>>(new PasswordHasher<User>());
        _provider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Creating_a_task_through_the_real_service_writes_an_activity_log_row_via_MediatR()
    {
        var project = new Project { TenantId = _tenant.Id, Tenant = _tenant, Name = "Project" };
        DbContext.Projects.Add(project);
        await DbContext.SaveChangesAsync();

        var taskService = _provider.GetRequiredService<ITaskService>();
        var input = new CreateTaskInput(project.Id, "Real pipeline task", null, TaskPriority.Medium, null, null);

        var task = await taskService.CreateTaskAsync(_tenant.Id, input, default);

        var activity = await DbContext.ActivityLogs.SingleAsync(a => a.EntityId == task.Id);
        activity.ActivityType.Should().Be(ActivityType.TaskCreated);
        activity.Description.Should().Contain("Real pipeline task");
    }

    [Fact]
    public async Task Creating_a_project_through_the_real_service_writes_an_activity_log_row_via_MediatR()
    {
        var projectService = _provider.GetRequiredService<IProjectService>();

        var project = await projectService.CreateProjectAsync(_tenant.Id, "Real pipeline project", default);

        var activity = await DbContext.ActivityLogs.SingleAsync(a => a.EntityId == project.Id);
        activity.ActivityType.Should().Be(ActivityType.ProjectCreated);
    }

    [Fact]
    public async Task Inviting_a_member_through_the_real_service_writes_an_activity_log_row_via_MediatR()
    {
        var owner = new User { Email = "owner@example.com", PasswordHash = "unused" };
        var ownerMembership = new TenantMember { TenantId = _tenant.Id, Tenant = _tenant, UserId = owner.Id, User = owner, Role = TenantRole.Owner };
        DbContext.AddRange(owner, ownerMembership);
        await DbContext.SaveChangesAsync();

        var teamService = _provider.GetRequiredService<ITeamService>();
        var invitation = await teamService.InviteAsync(_tenant.Id, owner.Id, new InviteMemberInput("invitee@example.com", TenantRole.Member), default);

        var activity = await DbContext.ActivityLogs.SingleAsync(a => a.EntityId == invitation.Invitation.Id);
        activity.ActivityType.Should().Be(ActivityType.MemberInvited);
        activity.UserId.Should().Be(owner.Id);
    }

    // Transaction-failure coverage: PublishSafeAsync (TaskService/
    // ProjectService/TeamService) only ever runs after SaveChangesAsync has
    // already committed — these prove that holds by forcing failures at two
    // different points (validation, before any write at all; a genuine
    // concurrency conflict, after the row already exists) and confirming no
    // ActivityLog row appears either way.
    [Fact]
    public async Task A_task_that_fails_validation_never_gets_an_activity_log_row()
    {
        var taskService = _provider.GetRequiredService<ITaskService>();
        // ProjectId doesn't exist for this tenant — CreateTaskInputValidator rejects it before any write.
        var input = new CreateTaskInput(Guid.NewGuid(), "Never created", null, TaskPriority.Medium, null, null);

        var act = () => taskService.CreateTaskAsync(_tenant.Id, input, default);

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
        (await DbContext.ActivityLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_task_update_that_hits_a_concurrency_conflict_never_gets_an_activity_log_row()
    {
        var project = new Project { TenantId = _tenant.Id, Tenant = _tenant, Name = "Project" };
        DbContext.Projects.Add(project);
        await DbContext.SaveChangesAsync();

        var taskService = _provider.GetRequiredService<ITaskService>();
        var task = await taskService.CreateTaskAsync(
            _tenant.Id, new CreateTaskInput(project.Id, "Title", null, TaskPriority.Medium, null, null), default);
        var originalVersion = task.Version;

        // First update succeeds and bumps the version (and does get an
        // activity log row — this is the baseline the second call's failure
        // is measured against, not itself the thing under test).
        await taskService.UpdateTaskAsync(task.Id, TitleOnlyUpdate("Updated once"), originalVersion, default);
        var countAfterFirstUpdate = await DbContext.ActivityLogs.CountAsync();

        // Retrying with the now-stale original version must fail...
        var act = () => taskService.UpdateTaskAsync(task.Id, TitleOnlyUpdate("Updated twice"), originalVersion, default);

        await act.Should().ThrowAsync<DevFlow.Application.Common.Exceptions.ConcurrencyConflictException<TaskItem>>();
        // ...and must not have logged anything beyond what the first (successful) update already logged.
        (await DbContext.ActivityLogs.CountAsync()).Should().Be(countAfterFirstUpdate);
    }

    // UpdateTaskInput has six positional parameters — this local helper
    // keeps the concurrency test above readable (only Title varies).
    private static UpdateTaskInput TitleOnlyUpdate(string title) =>
        new(title, null, null, null, null, null);

    private sealed class CapturingLoggerProvider : Microsoft.Extensions.Logging.ILoggerProvider
    {
        private readonly List<string> _logs;
        public CapturingLoggerProvider(List<string> logs) => _logs = logs;
        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new CapturingLogger(_logs);
        public void Dispose() { }

        private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
        {
            private readonly List<string> _logs;
            public CapturingLogger(List<string> logs) => _logs = logs;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

            public void Log<TState>(
                Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                _logs.Add($"[{logLevel}] {formatter(state, exception)} {exception}");
            }
        }
    }
}
