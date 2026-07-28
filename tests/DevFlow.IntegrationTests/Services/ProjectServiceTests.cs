using DevFlow.Application.Projects;
using DevFlow.Application.Realtime;
using DevFlow.Domain.Entities;
using DevFlow.IntegrationTests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevFlow.IntegrationTests.Services;

public class ProjectServiceTests : SqliteContextFixture
{
    private readonly IProjectService _service;
    private readonly RecordingPublisher _publisher = new();

    public ProjectServiceTests()
    {
        _service = new ProjectService(DbContext, _publisher, NullLogger<ProjectService>.Instance);
    }

    [Fact]
    public async Task CreateProjectAsync_persists_a_project_owned_by_the_given_tenant()
    {
        var tenant = new Tenant { Name = "Tenant" };
        DbContext.Tenants.Add(tenant);
        await DbContext.SaveChangesAsync();

        var project = await _service.CreateProjectAsync(tenant.Id, "New Project", default);

        project.Id.Should().NotBe(Guid.Empty);
        project.TenantId.Should().Be(tenant.Id);
        project.Name.Should().Be("New Project");
        project.IsArchived.Should().BeFalse();

        _publisher.Published.Should().ContainSingle().Which.Should().BeOfType<ProjectCreatedNotification>();
    }

    [Fact]
    public async Task GetProjectByIdAsync_returns_null_for_another_tenants_project()
    {
        var tenant = new Tenant { Name = "Tenant" };
        DbContext.Tenants.Add(tenant);
        await DbContext.SaveChangesAsync();

        CurrentUser.TenantId = tenant.Id;
        var project = await _service.CreateProjectAsync(tenant.Id, "Project", default);

        CurrentUser.TenantId = Guid.NewGuid(); // a different tenant
        var result = await _service.GetProjectByIdAsync(project.Id, default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateProjectAsync_applies_only_provided_fields()
    {
        var tenant = new Tenant { Name = "Tenant" };
        DbContext.Tenants.Add(tenant);
        await DbContext.SaveChangesAsync();
        CurrentUser.TenantId = tenant.Id;

        var project = await _service.CreateProjectAsync(tenant.Id, "Original Name", default);
        _publisher.Published.Clear();

        var updated = await _service.UpdateProjectAsync(project.Id, name: null, isArchived: true, default);

        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Original Name"); // untouched
        updated.IsArchived.Should().BeTrue();

        _publisher.Published.Should().ContainSingle().Which.Should().BeOfType<ProjectArchivedNotification>();
    }

    [Fact]
    public async Task UpdateProjectAsync_unarchiving_does_not_publish_ProjectArchivedNotification()
    {
        var tenant = new Tenant { Name = "Tenant" };
        DbContext.Tenants.Add(tenant);
        await DbContext.SaveChangesAsync();
        CurrentUser.TenantId = tenant.Id;

        var project = await _service.CreateProjectAsync(tenant.Id, "Project", default);
        await _service.UpdateProjectAsync(project.Id, name: null, isArchived: true, default);
        _publisher.Published.Clear();

        await _service.UpdateProjectAsync(project.Id, name: null, isArchived: false, default);

        _publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteProjectAsync_removes_the_project_and_returns_true()
    {
        var tenant = new Tenant { Name = "Tenant" };
        DbContext.Tenants.Add(tenant);
        await DbContext.SaveChangesAsync();
        CurrentUser.TenantId = tenant.Id;

        var project = await _service.CreateProjectAsync(tenant.Id, "Project", default);

        var deleted = await _service.DeleteProjectAsync(project.Id, default);
        var afterDelete = await _service.GetProjectByIdAsync(project.Id, default);

        deleted.Should().BeTrue();
        afterDelete.Should().BeNull();
    }

    [Fact]
    public async Task DeleteProjectAsync_returns_false_for_a_nonexistent_project()
    {
        var deleted = await _service.DeleteProjectAsync(Guid.NewGuid(), default);

        deleted.Should().BeFalse();
    }
}
