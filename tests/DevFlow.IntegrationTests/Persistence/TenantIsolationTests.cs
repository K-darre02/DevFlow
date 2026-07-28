using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
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
    public async Task TenantMembers_query_only_returns_rows_for_the_current_tenant()
    {
        var tenantA = new Tenant { Name = "Tenant A" };
        var tenantB = new Tenant { Name = "Tenant B" };
        var user = new User { Email = "person@example.com", PasswordHash = "hash" };
        var membershipA = new TenantMember { TenantId = tenantA.Id, Tenant = tenantA, UserId = user.Id, User = user, Role = TenantRole.Owner };
        var membershipB = new TenantMember { TenantId = tenantB.Id, Tenant = tenantB, UserId = user.Id, User = user, Role = TenantRole.Member };

        DbContext.AddRange(tenantA, tenantB, user, membershipA, membershipB);
        await DbContext.SaveChangesAsync();

        // Same underlying User, two separate memberships — proves
        // TenantMember (not User) is what carries tenant scoping now.
        CurrentUser.TenantId = tenantA.Id;
        var visibleToA = await DbContext.TenantMembers.ToListAsync();

        CurrentUser.TenantId = tenantB.Id;
        var visibleToB = await DbContext.TenantMembers.ToListAsync();

        visibleToA.Should().ContainSingle().Which.Role.Should().Be(TenantRole.Owner);
        visibleToB.Should().ContainSingle().Which.Role.Should().Be(TenantRole.Member);
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
    public async Task Users_are_visible_regardless_of_the_current_tenant()
    {
        // User is deliberately global (no query filter) — see User.cs.
        // Login has to find a user by email before any tenant is known at
        // all, which is only possible because this table isn't scoped.
        var user = new User { Email = "person@example.com", PasswordHash = "hash" };
        DbContext.Users.Add(user);
        await DbContext.SaveChangesAsync();

        CurrentUser.TenantId = null;
        var found = await DbContext.Users.FirstOrDefaultAsync(u => u.Email == user.Email);

        found.Should().NotBeNull();
    }

    [Fact]
    public async Task IgnoreQueryFilters_bypasses_tenant_scoping_for_pre_auth_membership_lookups()
    {
        var tenant = new Tenant { Name = "Tenant" };
        var user = new User { Email = "person@example.com", PasswordHash = "hash" };
        var membership = new TenantMember { TenantId = tenant.Id, Tenant = tenant, UserId = user.Id, User = user, Role = TenantRole.Owner };

        DbContext.AddRange(tenant, user, membership);
        await DbContext.SaveChangesAsync();

        // No authenticated tenant at all (as during Login, which uses
        // exactly this query to discover which tenant to issue a token for).
        CurrentUser.TenantId = null;

        var scoped = await DbContext.TenantMembers.FirstOrDefaultAsync(m => m.UserId == user.Id);
        var unscoped = await DbContext.TenantMembers.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.UserId == user.Id);

        scoped.Should().BeNull();
        unscoped.Should().NotBeNull();
    }
}
