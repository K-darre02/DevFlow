using DevFlow.Application.Common.Exceptions;
using DevFlow.Application.Realtime;
using DevFlow.Application.Tasks;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using DevFlow.IntegrationTests.TestSupport;
using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevFlow.IntegrationTests.Services;

public class TaskServiceTests : SqliteContextFixture
{
    private readonly ITaskService _service;
    private readonly Tenant _tenant;
    private readonly Project _project;
    private readonly RecordingPublisher _publisher = new();

    public TaskServiceTests()
    {
        _service = new TaskService(
            DbContext,
            new CreateTaskInputValidator(DbContext),
            new UpdateTaskInputValidator(DbContext),
            _publisher,
            NullLogger<TaskService>.Instance);

        _tenant = new Tenant { Name = "Tenant" };
        _project = new Project { TenantId = _tenant.Id, Tenant = _tenant, Name = "Project" };
        DbContext.AddRange(_tenant, _project);
        DbContext.SaveChangesAsync().GetAwaiter().GetResult();

        CurrentUser.TenantId = _tenant.Id;
    }

    [Fact]
    public async Task CreateTaskAsync_persists_a_task_with_the_given_fields()
    {
        var input = new CreateTaskInput(_project.Id, "Write tests", "Cover the happy path", TaskPriority.High, null, null);

        var task = await _service.CreateTaskAsync(_tenant.Id, input, default);

        task.Id.Should().NotBe(Guid.Empty);
        task.ProjectId.Should().Be(_project.Id);
        task.Title.Should().Be("Write tests");
        task.Priority.Should().Be(TaskPriority.High);
        task.Status.Should().Be(TaskItemStatus.Backlog); // default
        task.Version.Should().Be(1);

        _publisher.Published.Should().ContainSingle().Which.Should().BeOfType<TaskCreatedNotification>();
    }

    [Fact]
    public async Task CreateTaskAsync_throws_when_the_project_does_not_exist_for_this_tenant()
    {
        var input = new CreateTaskInput(Guid.NewGuid(), "Orphan task", null, TaskPriority.Medium, null, null);

        var act = () => _service.CreateTaskAsync(_tenant.Id, input, default);

        await act.Should().ThrowAsync<ValidationException>();
        _publisher.Published.Should().BeEmpty(); // validation failed before any write — nothing to broadcast
    }

    [Fact]
    public async Task CreateTaskAsync_throws_when_the_project_belongs_to_another_tenant()
    {
        var otherTenant = new Tenant { Name = "Other Tenant" };
        var otherProject = new Project { TenantId = otherTenant.Id, Tenant = otherTenant, Name = "Other Project" };
        DbContext.AddRange(otherTenant, otherProject);
        await DbContext.SaveChangesAsync();

        // Still scoped to _tenant — otherProject is invisible to the validator's
        // tenant-filtered existence check, exactly as it should be.
        var input = new CreateTaskInput(otherProject.Id, "Cross-tenant task", null, TaskPriority.Medium, null, null);

        var act = () => _service.CreateTaskAsync(_tenant.Id, input, default);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task CreateTaskAsync_throws_when_the_assignee_does_not_exist()
    {
        var input = new CreateTaskInput(_project.Id, "Assigned task", null, TaskPriority.Medium, Guid.NewGuid(), null);

        var act = () => _service.CreateTaskAsync(_tenant.Id, input, default);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task UpdateTaskAsync_applies_only_provided_fields_and_bumps_version()
    {
        var created = await _service.CreateTaskAsync(_tenant.Id, new CreateTaskInput(_project.Id, "Title", "Description", TaskPriority.Low, null, null), default);
        var versionBeforeUpdate = created.Version; // EF's identity map returns the same
                                                     // tracked instance below, so `created`
                                                     // itself mutates in place — capture the
                                                     // pre-update value now, not after.
        _publisher.Published.Clear(); // drop the TaskCreatedNotification from setup above

        var update = new UpdateTaskInput(Title: null, Description: null, Status: null, Priority: TaskPriority.Urgent, AssigneeUserId: null, DueDate: null);
        var updated = await _service.UpdateTaskAsync(created.Id, update, versionBeforeUpdate, default);

        updated.Should().NotBeNull();
        updated!.Title.Should().Be("Title"); // untouched
        updated.Priority.Should().Be(TaskPriority.Urgent);
        updated.Version.Should().Be(versionBeforeUpdate + 1);

        // Priority is neither Status nor AssigneeUserId — the generic event, not Moved/Assigned/Completed.
        _publisher.Published.Should().ContainSingle().Which.Should().BeOfType<TaskUpdatedNotification>();
    }

    [Fact]
    public async Task UpdateTaskAsync_setting_status_to_Done_sets_CompletedAt()
    {
        var created = await _service.CreateTaskAsync(_tenant.Id, new CreateTaskInput(_project.Id, "Title", null, TaskPriority.Medium, null, null), default);

        var update = new UpdateTaskInput(null, null, TaskItemStatus.Done, null, null, null);
        var updated = await _service.UpdateTaskAsync(created.Id, update, created.Version, default);

        updated!.Status.Should().Be(TaskItemStatus.Done);
        updated.CompletedAt.Should().NotBeNull();

        // Moving into Done fires both: it's a column move *and* a completion.
        _publisher.Published.Should().Contain(n => n is TaskMovedNotification);
        _publisher.Published.Should().Contain(n => n is TaskCompletedNotification);
    }

    [Fact]
    public async Task UpdateTaskAsync_moving_status_away_from_Done_clears_CompletedAt()
    {
        var created = await _service.CreateTaskAsync(_tenant.Id, new CreateTaskInput(_project.Id, "Title", null, TaskPriority.Medium, null, null), default);
        var done = await _service.UpdateTaskAsync(created.Id, new UpdateTaskInput(null, null, TaskItemStatus.Done, null, null, null), created.Version, default);
        _publisher.Published.Clear();

        var reopened = await _service.UpdateTaskAsync(done!.Id, new UpdateTaskInput(null, null, TaskItemStatus.InProgress, null, null, null), done.Version, default);

        reopened!.Status.Should().Be(TaskItemStatus.InProgress);
        reopened.CompletedAt.Should().BeNull();

        _publisher.Published.Should().ContainSingle().Which.Should().BeOfType<TaskMovedNotification>();
    }

    [Fact]
    public async Task UpdateTaskAsync_throws_ConcurrencyConflict_when_the_expected_version_is_stale()
    {
        var created = await _service.CreateTaskAsync(_tenant.Id, new CreateTaskInput(_project.Id, "Title", null, TaskPriority.Medium, null, null), default);
        var originalVersion = created.Version; // see comment in the test above — must
                                                // capture before any update mutates `created`.

        // First update succeeds and bumps the version...
        await _service.UpdateTaskAsync(created.Id, new UpdateTaskInput("Updated once", null, null, null, null, null), originalVersion, default);
        _publisher.Published.Clear();

        // ...so retrying with the *original* (now stale) version must fail.
        var act = () => _service.UpdateTaskAsync(created.Id, new UpdateTaskInput("Updated twice", null, null, null, null, null), originalVersion, default);

        var exception = await act.Should().ThrowAsync<ConcurrencyConflictException<TaskItem>>();
        exception.Which.CurrentState.Title.Should().Be("Updated once");
        _publisher.Published.Should().BeEmpty(); // the write failed — nothing to broadcast
    }

    [Fact]
    public async Task UpdateTaskAsync_changing_the_assignee_publishes_TaskAssignedNotification()
    {
        var created = await _service.CreateTaskAsync(_tenant.Id, new CreateTaskInput(_project.Id, "Title", null, TaskPriority.Medium, null, null), default);
        var user = new User { Email = "assignee@example.com", PasswordHash = "unused" };
        var membership = new TenantMember { TenantId = _tenant.Id, Tenant = _tenant, UserId = user.Id, User = user, Role = TenantRole.Member };
        DbContext.AddRange(user, membership);
        await DbContext.SaveChangesAsync();
        _publisher.Published.Clear();

        var updated = await _service.UpdateTaskAsync(
            created.Id, new UpdateTaskInput(null, null, null, null, user.Id, null), created.Version, default);

        updated!.AssigneeUserId.Should().Be(user.Id);
        _publisher.Published.Should().ContainSingle().Which.Should().BeOfType<TaskAssignedNotification>();
    }

    [Fact]
    public async Task DeleteTaskAsync_publishes_TaskDeletedNotification()
    {
        var created = await _service.CreateTaskAsync(_tenant.Id, new CreateTaskInput(_project.Id, "Title", null, TaskPriority.Medium, null, null), default);
        _publisher.Published.Clear();

        var deleted = await _service.DeleteTaskAsync(created.Id, default);

        deleted.Should().BeTrue();
        _publisher.Published.Should().ContainSingle().Which.Should().BeOfType<TaskDeletedNotification>();
    }

    [Fact]
    public async Task UpdateTaskAsync_returns_null_for_a_task_in_another_tenant()
    {
        var otherTenant = new Tenant { Name = "Other Tenant" };
        var otherProject = new Project { TenantId = otherTenant.Id, Tenant = otherTenant, Name = "Other Project" };
        var otherTask = new TaskItem { TenantId = otherTenant.Id, Tenant = otherTenant, ProjectId = otherProject.Id, Project = otherProject, Title = "Other Task" };
        DbContext.AddRange(otherTenant, otherProject, otherTask);
        await DbContext.SaveChangesAsync();

        // Still scoped to _tenant.
        var result = await _service.UpdateTaskAsync(otherTask.Id, new UpdateTaskInput("Hijacked", null, null, null, null, null), otherTask.Version, default);

        result.Should().BeNull();
    }
}
