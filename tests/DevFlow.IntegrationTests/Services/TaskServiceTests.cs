using DevFlow.Application.Common.Exceptions;
using DevFlow.Application.Tasks;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using DevFlow.IntegrationTests.TestSupport;
using FluentAssertions;
using FluentValidation;
using Xunit;

namespace DevFlow.IntegrationTests.Services;

public class TaskServiceTests : SqliteContextFixture
{
    private readonly ITaskService _service;
    private readonly Tenant _tenant;
    private readonly Project _project;

    public TaskServiceTests()
    {
        _service = new TaskService(DbContext, new CreateTaskInputValidator(DbContext), new UpdateTaskInputValidator(DbContext));

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
    }

    [Fact]
    public async Task CreateTaskAsync_throws_when_the_project_does_not_exist_for_this_tenant()
    {
        var input = new CreateTaskInput(Guid.NewGuid(), "Orphan task", null, TaskPriority.Medium, null, null);

        var act = () => _service.CreateTaskAsync(_tenant.Id, input, default);

        await act.Should().ThrowAsync<ValidationException>();
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

        var update = new UpdateTaskInput(Title: null, Description: null, Status: null, Priority: TaskPriority.Urgent, AssigneeUserId: null, DueDate: null);
        var updated = await _service.UpdateTaskAsync(created.Id, update, versionBeforeUpdate, default);

        updated.Should().NotBeNull();
        updated!.Title.Should().Be("Title"); // untouched
        updated.Priority.Should().Be(TaskPriority.Urgent);
        updated.Version.Should().Be(versionBeforeUpdate + 1);
    }

    [Fact]
    public async Task UpdateTaskAsync_setting_status_to_Done_sets_CompletedAt()
    {
        var created = await _service.CreateTaskAsync(_tenant.Id, new CreateTaskInput(_project.Id, "Title", null, TaskPriority.Medium, null, null), default);

        var update = new UpdateTaskInput(null, null, TaskItemStatus.Done, null, null, null);
        var updated = await _service.UpdateTaskAsync(created.Id, update, created.Version, default);

        updated!.Status.Should().Be(TaskItemStatus.Done);
        updated.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateTaskAsync_moving_status_away_from_Done_clears_CompletedAt()
    {
        var created = await _service.CreateTaskAsync(_tenant.Id, new CreateTaskInput(_project.Id, "Title", null, TaskPriority.Medium, null, null), default);
        var done = await _service.UpdateTaskAsync(created.Id, new UpdateTaskInput(null, null, TaskItemStatus.Done, null, null, null), created.Version, default);

        var reopened = await _service.UpdateTaskAsync(done!.Id, new UpdateTaskInput(null, null, TaskItemStatus.InProgress, null, null, null), done.Version, default);

        reopened!.Status.Should().Be(TaskItemStatus.InProgress);
        reopened.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task UpdateTaskAsync_throws_ConcurrencyConflict_when_the_expected_version_is_stale()
    {
        var created = await _service.CreateTaskAsync(_tenant.Id, new CreateTaskInput(_project.Id, "Title", null, TaskPriority.Medium, null, null), default);
        var originalVersion = created.Version; // see comment in the test above — must
                                                // capture before any update mutates `created`.

        // First update succeeds and bumps the version...
        await _service.UpdateTaskAsync(created.Id, new UpdateTaskInput("Updated once", null, null, null, null, null), originalVersion, default);

        // ...so retrying with the *original* (now stale) version must fail.
        var act = () => _service.UpdateTaskAsync(created.Id, new UpdateTaskInput("Updated twice", null, null, null, null, null), originalVersion, default);

        var exception = await act.Should().ThrowAsync<ConcurrencyConflictException<TaskItem>>();
        exception.Which.CurrentState.Title.Should().Be("Updated once");
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
