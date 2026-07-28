using DevFlow.Application.Activities;
using DevFlow.Application.Dashboard;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using DevFlow.IntegrationTests.TestSupport;
using FluentAssertions;
using Xunit;

namespace DevFlow.IntegrationTests.Dashboard;

public class DashboardServiceTests : SqliteContextFixture
{
    private readonly IDashboardService _service;
    private readonly Tenant _tenant;
    private readonly Project _project;

    public DashboardServiceTests()
    {
        // A real ActivityLogService, not a fake — DashboardService composes
        // it directly (see its own comment on why), so this is what proves
        // that composition actually works, not just that DashboardService's
        // own queries do.
        _service = new DashboardService(DbContext, new ActivityLogService(DbContext));

        _tenant = new Tenant { Name = "Tenant" };
        _project = new Project { TenantId = _tenant.Id, Tenant = _tenant, Name = "Project" };
        DbContext.AddRange(_tenant, _project);
        DbContext.SaveChangesAsync().GetAwaiter().GetResult();

        CurrentUser.TenantId = _tenant.Id;
    }

    private TaskItem NewTask(
        TaskItemStatus status = TaskItemStatus.ToDo,
        TaskPriority priority = TaskPriority.Medium,
        DateOnly? dueDate = null,
        string title = "Task") =>
        new()
        {
            TenantId = _tenant.Id,
            Tenant = _tenant,
            ProjectId = _project.Id,
            Project = _project,
            Title = title,
            Status = status,
            Priority = priority,
            DueDate = dueDate
        };

    [Fact]
    public async Task GetSummaryAsync_counts_only_non_archived_projects()
    {
        var archived = new Project { TenantId = _tenant.Id, Tenant = _tenant, Name = "Archived", IsArchived = true };
        DbContext.Projects.Add(archived);
        await DbContext.SaveChangesAsync();

        var summary = await _service.GetSummaryAsync(default);

        // _project (from the constructor) + this test's own archived one —
        // only the former should count.
        summary.ProjectCount.Should().Be(1);
    }

    [Fact]
    public async Task GetSummaryAsync_counts_all_tasks_and_completed_tasks()
    {
        DbContext.AddRange(
            NewTask(TaskItemStatus.ToDo),
            NewTask(TaskItemStatus.Done),
            NewTask(TaskItemStatus.Done));
        await DbContext.SaveChangesAsync();

        var summary = await _service.GetSummaryAsync(default);

        summary.TaskCount.Should().Be(3);
        summary.CompletedTaskCount.Should().Be(2);
    }

    [Fact]
    public async Task GetSummaryAsync_counts_overdue_tasks_correctly()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        DbContext.AddRange(
            NewTask(TaskItemStatus.ToDo, dueDate: today.AddDays(-1), title: "Overdue"),
            NewTask(TaskItemStatus.Done, dueDate: today.AddDays(-1), title: "Overdue but done"),
            NewTask(TaskItemStatus.ToDo, dueDate: today, title: "Due today"),
            NewTask(TaskItemStatus.ToDo, dueDate: today.AddDays(1), title: "Due tomorrow"),
            NewTask(TaskItemStatus.ToDo, dueDate: null, title: "No due date"));
        await DbContext.SaveChangesAsync();

        var summary = await _service.GetSummaryAsync(default);

        // Only the genuinely past-due, not-yet-done task counts — done
        // tasks, tasks due today, and tasks with no due date all don't.
        summary.OverdueTaskCount.Should().Be(1);
    }

    [Fact]
    public async Task GetSummaryAsync_groups_tasks_by_status()
    {
        DbContext.AddRange(
            NewTask(TaskItemStatus.ToDo),
            NewTask(TaskItemStatus.ToDo),
            NewTask(TaskItemStatus.InProgress));
        await DbContext.SaveChangesAsync();

        var summary = await _service.GetSummaryAsync(default);

        summary.TasksByStatus.Should().Contain(x => x.Status == TaskItemStatus.ToDo && x.Count == 2);
        summary.TasksByStatus.Should().Contain(x => x.Status == TaskItemStatus.InProgress && x.Count == 1);
    }

    [Fact]
    public async Task GetSummaryAsync_groups_tasks_by_priority()
    {
        DbContext.AddRange(
            NewTask(priority: TaskPriority.High),
            NewTask(priority: TaskPriority.High),
            NewTask(priority: TaskPriority.Low));
        await DbContext.SaveChangesAsync();

        var summary = await _service.GetSummaryAsync(default);

        summary.TasksByPriority.Should().Contain(x => x.Priority == TaskPriority.High && x.Count == 2);
        summary.TasksByPriority.Should().Contain(x => x.Priority == TaskPriority.Low && x.Count == 1);
    }

    [Fact]
    public async Task GetSummaryAsync_returns_recent_activity_newest_first()
    {
        var first = new ActivityLog { TenantId = _tenant.Id, Tenant = _tenant, ActivityType = ActivityType.ProjectCreated, EntityType = ActivityEntityType.Project, EntityId = Guid.NewGuid(), Description = "First" };
        DbContext.ActivityLogs.Add(first);
        await DbContext.SaveChangesAsync();
        var second = new ActivityLog { TenantId = _tenant.Id, Tenant = _tenant, ActivityType = ActivityType.TaskCreated, EntityType = ActivityEntityType.Task, EntityId = Guid.NewGuid(), Description = "Second" };
        DbContext.ActivityLogs.Add(second);
        await DbContext.SaveChangesAsync();

        var summary = await _service.GetSummaryAsync(default);

        summary.RecentActivity.Select(a => a.Description).Should().ContainInOrder("Second", "First");
    }

    [Fact]
    public async Task GetSummaryAsync_returns_overdue_tasks_ordered_oldest_due_date_first_and_capped_at_five()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // 6 overdue tasks, due dates spread across the past week — only the
        // 5 *oldest* due dates should come back.
        for (var i = 1; i <= 6; i++)
        {
            DbContext.TaskItems.Add(NewTask(TaskItemStatus.ToDo, dueDate: today.AddDays(-i), title: $"Overdue {i}"));
        }
        await DbContext.SaveChangesAsync();

        var summary = await _service.GetSummaryAsync(default);

        summary.OverdueTasks.Should().HaveCount(5);
        summary.OverdueTasks.Select(t => t.Title).Should().ContainInOrder("Overdue 6", "Overdue 5", "Overdue 4", "Overdue 3", "Overdue 2");
        summary.OverdueTasks.Should().OnlyContain(t => t.Project != null);
    }

    [Fact]
    public async Task GetSummaryAsync_only_reflects_the_current_tenants_data()
    {
        var otherTenant = new Tenant { Name = "Other Tenant" };
        var otherProject = new Project { TenantId = otherTenant.Id, Tenant = otherTenant, Name = "Other Project" };
        var otherTask = new TaskItem { TenantId = otherTenant.Id, Tenant = otherTenant, ProjectId = otherProject.Id, Project = otherProject, Title = "Other tenant's task" };
        DbContext.AddRange(otherTenant, otherProject, otherTask);
        await DbContext.SaveChangesAsync();

        DbContext.TaskItems.Add(NewTask(title: "This tenant's task"));
        await DbContext.SaveChangesAsync();

        var summary = await _service.GetSummaryAsync(default);

        summary.ProjectCount.Should().Be(1);
        summary.TaskCount.Should().Be(1);
        summary.TasksByStatus.Sum(x => x.Count).Should().Be(1);
    }
}
