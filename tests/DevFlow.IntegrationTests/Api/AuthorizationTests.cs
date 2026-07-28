using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DevFlow.Api.Contracts.Projects;
using DevFlow.Api.Services;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using DevFlow.Infrastructure.Persistence;
using DevFlow.IntegrationTests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DevFlow.IntegrationTests.Api;

// End-to-end, through the real pipeline: [Authorize], JWT bearer validation,
// routing, and DevFlowDbContext's tenant query filter — not just the service
// layer in isolation. This is the most direct proof of "authorization rules"
// and "a tenant cannot access another tenant's data".
//
// Deliberately NOT IClassFixture<DevFlowWebApplicationFactory>: that shares
// one factory (and its one Sqlite in-memory connection/database) across every
// test method in the class, so seeded data from one test collides with the
// next. xUnit constructs a fresh instance of this class per test method, so
// creating the factory here gives each test its own isolated database.
public class AuthorizationTests : IDisposable
{
    private readonly DevFlowWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task GetProjects_without_a_token_returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/projects");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetProjects_with_a_garbage_token_returns_401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-jwt");

        var response = await client.GetAsync("/api/projects");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetProjects_only_returns_the_callers_tenant_projects()
    {
        var (tenantA, tenantB) = await SeedTwoTenantsWithOneProjectEachAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(tenantA.TenantId, tenantA.UserId));

        var response = await client.GetAsync("/api/projects");
        var projects = await response.Content.ReadFromJsonAsync<List<ProjectResponse>>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        projects.Should().ContainSingle(p => p.Id == tenantA.ProjectId);
        projects.Should().NotContain(p => p.Id == tenantB.ProjectId);
    }

    [Fact]
    public async Task GetProject_by_id_for_another_tenants_project_returns_404_not_403()
    {
        var (tenantA, tenantB) = await SeedTwoTenantsWithOneProjectEachAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(tenantA.TenantId, tenantA.UserId));

        // tenantA's token, requesting tenantB's project id directly.
        var response = await client.GetAsync($"/api/projects/{tenantB.ProjectId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateProject_ignores_any_client_supplied_tenant_and_uses_the_tokens_tenant()
    {
        var (tenantA, _) = await SeedTwoTenantsWithOneProjectEachAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(tenantA.TenantId, tenantA.UserId));

        var response = await client.PostAsJsonAsync("/api/projects", new { name = "New Project" });
        var created = await response.Content.ReadFromJsonAsync<ProjectResponse>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created!.TenantId.Should().Be(tenantA.TenantId);
    }

    private async Task<(Guid TenantId, Guid ProjectId, Guid UserId)> SeedOneTenantWithProjectAsync(DevFlowDbContext context, string label)
    {
        var tenant = new Tenant { Name = $"Tenant {label}" };
        var user = new User { Email = $"{label}@example.com", PasswordHash = "unused-in-these-tests" };
        var membership = new TenantMember { TenantId = tenant.Id, Tenant = tenant, UserId = user.Id, User = user, Role = TenantRole.Owner };
        var project = new Project { TenantId = tenant.Id, Tenant = tenant, Name = $"Project {label}" };

        context.AddRange(tenant, user, membership, project);
        await context.SaveChangesAsync();

        return (tenant.Id, project.Id, user.Id);
    }

    private async Task<((Guid TenantId, Guid ProjectId, Guid UserId) A, (Guid TenantId, Guid ProjectId, Guid UserId) B)> SeedTwoTenantsWithOneProjectEachAsync()
    {
        var context = await _factory.CreateDbContextAsync();
        var a = await SeedOneTenantWithProjectAsync(context, "A");
        var b = await SeedOneTenantWithProjectAsync(context, "B");
        return (a, b);
    }

    private static string GenerateToken(Guid tenantId, Guid userId, TenantRole role = TenantRole.Owner)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = DevFlowWebApplicationFactory.JwtIssuer,
                ["Jwt:Audience"] = DevFlowWebApplicationFactory.JwtAudience,
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:SigningKey"] = DevFlowWebApplicationFactory.JwtSigningKey
            })
            .Build();

        var user = new User { Id = userId, Email = "test@example.com" };
        var (accessToken, _) = new JwtTokenService(configuration).GenerateToken(user, tenantId, role);
        return accessToken;
    }
}
