using DevFlow.Application.Common;
using DevFlow.Domain.Entities;
using DevFlow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevFlow.IntegrationTests.Persistence;

// Proves the actual behavior of DevFlowDbContext's tenant query filters,
// rather than just trusting the LINQ reads correctly. Uses Sqlite in-memory
// instead of a real Postgres so it runs without Docker — see
// docs/devflow/04-security.md §2.
public class TenantIsolationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DevFlowDbContext _dbContext;
    private readonly FakeCurrentUserService _currentUser;

    public TenantIsolationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DevFlowDbContext>()
            .UseSqlite(_connection)
            .Options;

        _currentUser = new FakeCurrentUserService();
        _dbContext = new DevFlowDbContext(options, _currentUser);
        _dbContext.Database.EnsureCreated();
    }

    [Fact]
    public async Task Projects_query_only_returns_rows_for_the_current_tenant()
    {
        var tenantA = new Tenant { Name = "Tenant A" };
        var tenantB = new Tenant { Name = "Tenant B" };
        var projectA = new Project { TenantId = tenantA.Id, Tenant = tenantA, Name = "A's Project" };
        var projectB = new Project { TenantId = tenantB.Id, Tenant = tenantB, Name = "B's Project" };

        _dbContext.AddRange(tenantA, tenantB, projectA, projectB);
        await _dbContext.SaveChangesAsync();

        // Same DbContext instance, same compiled model, different resolved
        // tenant — this is exactly what proves the query filter reads
        // _currentUserService.TenantId live per query rather than a value
        // baked in once when the model was first built.
        _currentUser.TenantId = tenantA.Id;
        var visibleToA = await _dbContext.Projects.ToListAsync();

        _currentUser.TenantId = tenantB.Id;
        var visibleToB = await _dbContext.Projects.ToListAsync();

        visibleToA.Should().ContainSingle().Which.Id.Should().Be(projectA.Id);
        visibleToB.Should().ContainSingle().Which.Id.Should().Be(projectB.Id);
    }

    [Fact]
    public async Task Projects_query_returns_nothing_when_no_tenant_is_resolved()
    {
        var tenant = new Tenant { Name = "Tenant" };
        var project = new Project { TenantId = tenant.Id, Tenant = tenant, Name = "Project" };

        _dbContext.AddRange(tenant, project);
        await _dbContext.SaveChangesAsync();

        _currentUser.TenantId = null;
        var visible = await _dbContext.Projects.ToListAsync();

        visible.Should().BeEmpty();
    }

    [Fact]
    public async Task IgnoreQueryFilters_bypasses_tenant_scoping_for_pre_auth_lookups()
    {
        var tenant = new Tenant { Name = "Tenant" };
        var user = new User { TenantId = tenant.Id, Tenant = tenant, Email = "person@example.com", PasswordHash = "hash" };

        _dbContext.AddRange(tenant, user);
        await _dbContext.SaveChangesAsync();

        // No authenticated tenant at all (as during login/register).
        _currentUser.TenantId = null;

        var scoped = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == user.Email);
        var unscoped = await _dbContext.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == user.Email);

        scoped.Should().BeNull();
        unscoped.Should().NotBeNull();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    private sealed class FakeCurrentUserService : ICurrentUserService
    {
        public Guid? UserId { get; set; }

        public Guid? TenantId { get; set; }
    }
}
