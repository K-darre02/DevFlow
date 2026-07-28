using DevFlow.Application.Activities;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using DevFlow.IntegrationTests.TestSupport;
using FluentAssertions;
using Xunit;

namespace DevFlow.IntegrationTests.Services;

public class ActivityLogServiceTests : SqliteContextFixture
{
    private readonly IActivityLogService _service;
    private readonly Tenant _tenant;
    private readonly User _user;

    public ActivityLogServiceTests()
    {
        _service = new ActivityLogService(DbContext);

        _tenant = new Tenant { Name = "Tenant" };
        _user = new User { Email = "actor@example.com", PasswordHash = "unused" };
        DbContext.AddRange(_tenant, _user);
        DbContext.SaveChangesAsync().GetAwaiter().GetResult();

        CurrentUser.TenantId = _tenant.Id;
    }

    private async Task<ActivityLog> SeedActivityAsync(ActivityType activityType, ActivityEntityType entityType, string description, Guid? userId = null)
    {
        var entry = new ActivityLog
        {
            TenantId = _tenant.Id,
            UserId = userId ?? _user.Id,
            ActivityType = activityType,
            EntityType = entityType,
            EntityId = Guid.NewGuid(),
            Description = description
        };
        DbContext.ActivityLogs.Add(entry);
        await DbContext.SaveChangesAsync();
        return entry;
    }

    [Fact]
    public async Task GetActivitiesAsync_returns_results_newest_first()
    {
        var first = await SeedActivityAsync(ActivityType.ProjectCreated, ActivityEntityType.Project, "First");
        var second = await SeedActivityAsync(ActivityType.TaskCreated, ActivityEntityType.Task, "Second");
        var third = await SeedActivityAsync(ActivityType.TaskDeleted, ActivityEntityType.Task, "Third");

        var result = await _service.GetActivitiesAsync(new ActivityLogQuery(1, 20, null, null, null), default);

        result.Items.Select(a => a.Id).Should().ContainInOrder(third.Id, second.Id, first.Id);
        result.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task GetActivitiesAsync_paginates_correctly()
    {
        for (var i = 0; i < 5; i++)
        {
            await SeedActivityAsync(ActivityType.TaskCreated, ActivityEntityType.Task, $"Activity {i}");
        }

        var pageOne = await _service.GetActivitiesAsync(new ActivityLogQuery(1, 2, null, null, null), default);
        var pageTwo = await _service.GetActivitiesAsync(new ActivityLogQuery(2, 2, null, null, null), default);

        pageOne.Items.Should().HaveCount(2);
        pageTwo.Items.Should().HaveCount(2);
        pageOne.TotalCount.Should().Be(5);
        pageOne.Items.Select(a => a.Id).Should().NotIntersectWith(pageTwo.Items.Select(a => a.Id));
    }

    [Fact]
    public async Task GetActivitiesAsync_filters_by_entity_type()
    {
        await SeedActivityAsync(ActivityType.ProjectCreated, ActivityEntityType.Project, "Project one");
        await SeedActivityAsync(ActivityType.TaskCreated, ActivityEntityType.Task, "Task one");

        var result = await _service.GetActivitiesAsync(new ActivityLogQuery(1, 20, ActivityEntityType.Project, null, null), default);

        result.Items.Should().ContainSingle().Which.EntityType.Should().Be(ActivityEntityType.Project);
    }

    [Fact]
    public async Task GetActivitiesAsync_filters_by_user()
    {
        var otherUser = new User { Email = "other@example.com", PasswordHash = "unused" };
        DbContext.Users.Add(otherUser);
        await DbContext.SaveChangesAsync();

        await SeedActivityAsync(ActivityType.TaskCreated, ActivityEntityType.Task, "By actor", _user.Id);
        await SeedActivityAsync(ActivityType.TaskCreated, ActivityEntityType.Task, "By other", otherUser.Id);

        var result = await _service.GetActivitiesAsync(new ActivityLogQuery(1, 20, null, otherUser.Id, null), default);

        result.Items.Should().ContainSingle().Which.Description.Should().Be("By other");
    }

    [Fact]
    public async Task GetActivitiesAsync_filters_by_activity_type()
    {
        await SeedActivityAsync(ActivityType.TaskCreated, ActivityEntityType.Task, "Created");
        await SeedActivityAsync(ActivityType.TaskDeleted, ActivityEntityType.Task, "Deleted");

        var result = await _service.GetActivitiesAsync(new ActivityLogQuery(1, 20, null, null, ActivityType.TaskDeleted), default);

        result.Items.Should().ContainSingle().Which.ActivityType.Should().Be(ActivityType.TaskDeleted);
    }

    [Fact]
    public async Task GetActivitiesAsync_only_returns_the_current_tenants_activities()
    {
        var otherTenant = new Tenant { Name = "Other Tenant" };
        DbContext.Tenants.Add(otherTenant);
        await DbContext.SaveChangesAsync();

        await SeedActivityAsync(ActivityType.TaskCreated, ActivityEntityType.Task, "This tenant");

        var otherEntry = new ActivityLog
        {
            TenantId = otherTenant.Id,
            ActivityType = ActivityType.TaskCreated,
            EntityType = ActivityEntityType.Task,
            EntityId = Guid.NewGuid(),
            Description = "Other tenant"
        };
        DbContext.ActivityLogs.Add(otherEntry);
        await DbContext.SaveChangesAsync();

        var result = await _service.GetActivitiesAsync(new ActivityLogQuery(1, 20, null, null, null), default);

        result.Items.Should().ContainSingle().Which.Description.Should().Be("This tenant");
    }
}
