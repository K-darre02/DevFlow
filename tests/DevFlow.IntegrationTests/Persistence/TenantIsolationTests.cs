using DevFlow.Domain.Entities;
using DevFlow.IntegrationTests.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevFlow.IntegrationTests.Persistence;

// Proves the actual behavior of DevFlowDbContext's tenant query filters,
// rather than just trusting the LINQ reads correctly. Uses Sqlite in-memory
// instead of a real Postgres so it runs without Docker — see
// docs/devflow/04-security.md §2.
public class TenantIsolationTests : SqliteContextFixture
{
    [Fact]
    public async Task Projects_query_only_returns_rows_for_the_current_tenant()
    {
        var tenantA = new Tenant { Name = "Tenant A" };
        var tenantB = new Tenant { Name = "Tenant B" };
        var projectA = new Project { TenantId = tenantA.Id, Tenant = tenantA, Name = "A's Project" };
        var projectB = new Project { TenantId = tenantB.Id, Tenant = tenantB, Name = "B's Project" };

        DbContext.AddRange(tenantA, tenantB, projectA, projectB);
        await DbContext.SaveChangesAsync();

        // Same DbContext instance, same compiled model, different resolved
        // tenant — this is exactly what proves the query filter reads
        // _currentUserService.TenantId live per query rather than a value
        // baked in once when the model was first built.
        CurrentUser.TenantId = tenantA.Id;
        var visibleToA = await DbContext.Projects.ToListAsync();

        CurrentUser.TenantId = tenantB.Id;
        var visibleToB = await DbContext.Projects.ToListAsync();

        visibleToA.Should().ContainSingle().Which.Id.Should().Be(projectA.Id);
        visibleToB.Should().ContainSingle().Which.Id.Should().Be(projectB.Id);
    }

    [Fact]
    public async Task Tasks_query_only_returns_rows_for_the_current_tenant()
    {
        var tenantA = new Tenant { Name = "Tenant A" };
        var tenantB = new Tenant { Name = "Tenant B" };
        var projectA = new Project { TenantId = tenantA.Id, Tenant = tenantA, Name = "A's Project" };
        var projectB = new Project { TenantId = tenantB.Id, Tenant = tenantB, Name = "B's Project" };
        var taskA = new TaskItem { TenantId = tenantA.Id, Tenant = tenantA, ProjectId = projectA.Id, Project = projectA, Title = "A's Task" };
        var taskB = new TaskItem { TenantId = tenantB.Id, Tenant = tenantB, ProjectId = projectB.Id, Project = projectB, Title = "B's Task" };

        DbContext.AddRange(tenantA, tenantB, projectA, projectB, taskA, taskB);
        await DbContext.SaveChangesAsync();

        CurrentUser.TenantId = tenantA.Id;
        var visibleToA = await DbContext.TaskItems.ToListAsync();
        var taskBFromA = await DbContext.TaskItems.FirstOrDefaultAsync(t => t.Id == taskB.Id);

        CurrentUser.TenantId = tenantB.Id;
        var visibleToB = await DbContext.TaskItems.ToListAsync();

        visibleToA.Should().ContainSingle().Which.Id.Should().Be(taskA.Id);
        taskBFromA.Should().BeNull(); // fetching tenant B's task by id while scoped to tenant A: not found, not a data leak
        visibleToB.Should().ContainSingle().Which.Id.Should().Be(taskB.Id);
    }

    [Fact]
    public async Task Projects_query_returns_nothing_when_no_tenant_is_resolved()
    {
        var tenant = new Tenant { Name = "Tenant" };
        var project = new Project { TenantId = tenant.Id, Tenant = tenant, Name = "Project" };

        DbContext.AddRange(tenant, project);
        await DbContext.SaveChangesAsync();

        CurrentUser.TenantId = null;
        var visible = await DbContext.Projects.ToListAsync();

        visible.Should().BeEmpty();
    }

    [Fact]
    public async Task IgnoreQueryFilters_bypasses_tenant_scoping_for_pre_auth_lookups()
    {
        var tenant = new Tenant { Name = "Tenant" };
        var user = new User { TenantId = tenant.Id, Tenant = tenant, Email = "person@example.com", PasswordHash = "hash" };

        DbContext.AddRange(tenant, user);
        await DbContext.SaveChangesAsync();

        // No authenticated tenant at all (as during login/register).
        CurrentUser.TenantId = null;

        var scoped = await DbContext.Users.FirstOrDefaultAsync(u => u.Email == user.Email);
        var unscoped = await DbContext.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == user.Email);

        scoped.Should().BeNull();
        unscoped.Should().NotBeNull();
    }
}
