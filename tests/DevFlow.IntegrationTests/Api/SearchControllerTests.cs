using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevFlow.Api.Contracts.Search;
using DevFlow.Api.Services;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using DevFlow.Infrastructure.Persistence;
using DevFlow.IntegrationTests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DevFlow.IntegrationTests.Api;

// End-to-end through the real pipeline (JWT auth, tenant resolution,
// LikeSearchService swapped in for Sqlite) — same rationale as
// DashboardControllerTests. LikeSearchServiceTests already covers matching
// and ranking behavior in isolation; this proves the grouped HTTP contract
// and permission/tenant-isolation boundary on top of it.
public class SearchControllerTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly DevFlowWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<(Guid TenantId, Guid UserId)> SeedTenantWithOwnerAsync(DevFlowDbContext context, string label)
    {
        var tenant = new Tenant { Name = $"Tenant {label}" };
        var user = new User { Email = $"{label}@example.com", PasswordHash = "unused" };
        var membership = new TenantMember { TenantId = tenant.Id, UserId = user.Id, Role = TenantRole.Owner };
        context.AddRange(tenant, user, membership);
        await context.SaveChangesAsync();
        return (tenant.Id, user.Id);
    }

    private static string GenerateToken(Guid tenantId, Guid userId)
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
        var (accessToken, _) = new JwtTokenService(configuration).GenerateToken(user, tenantId, TenantRole.Owner);
        return accessToken;
    }

    private HttpClient AuthenticatedClient(Guid tenantId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(tenantId, userId));
        return client;
    }

    private static async Task<Guid> CreateProjectAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/projects", new { name }, JsonOptions);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private static async Task CreateTaskAsync(HttpClient client, Guid projectId, string title)
    {
        var response = await client.PostAsJsonAsync("/api/tasks", new { projectId, title }, JsonOptions);
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Search_without_a_token_returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/search?q=anything");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Search_returns_grouped_results_matching_projects_and_tasks_created_through_the_real_endpoints()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantId, userId) = await SeedTenantWithOwnerAsync(context, "A");
        var client = AuthenticatedClient(tenantId, userId);
        var projectId = await CreateProjectAsync(client, "Voyager Launch");
        await CreateTaskAsync(client, projectId, "Voyager checklist review");

        var response = await client.GetAsync("/api/search?q=voyager");
        var result = await response.Content.ReadFromJsonAsync<SearchResponse>(JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result!.Projects.Items.Should().ContainSingle(p => p.Id == projectId && p.Name == "Voyager Launch");
        result.Tasks.Items.Should().ContainSingle(t => t.Title == "Voyager checklist review");
    }

    [Fact]
    public async Task Search_returns_empty_groups_for_a_query_that_matches_nothing()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantId, userId) = await SeedTenantWithOwnerAsync(context, "A");
        var client = AuthenticatedClient(tenantId, userId);
        await CreateProjectAsync(client, "Some Project");

        var response = await client.GetAsync("/api/search?q=no-such-term-xyz");
        var result = await response.Content.ReadFromJsonAsync<SearchResponse>(JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result!.Projects.Items.Should().BeEmpty();
        result.Tasks.Items.Should().BeEmpty();
        result.Users.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_with_a_missing_query_returns_empty_groups_rather_than_an_error()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantId, userId) = await SeedTenantWithOwnerAsync(context, "A");
        var client = AuthenticatedClient(tenantId, userId);

        var response = await client.GetAsync("/api/search");
        var result = await response.Content.ReadFromJsonAsync<SearchResponse>(JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result!.Projects.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_never_returns_another_tenants_projects_tasks_or_users()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantA, userA) = await SeedTenantWithOwnerAsync(context, "A");
        var (tenantB, userB) = await SeedTenantWithOwnerAsync(context, "B");

        var clientA = AuthenticatedClient(tenantA, userA);
        var projectA = await CreateProjectAsync(clientA, "Shared-Name Project");
        await CreateTaskAsync(clientA, projectA, "Shared-Name task A");

        var clientB = AuthenticatedClient(tenantB, userB);
        var projectB = await CreateProjectAsync(clientB, "Shared-Name Project");
        await CreateTaskAsync(clientB, projectB, "Shared-Name task B");

        var resultA = await (await clientA.GetAsync("/api/search?q=shared-name")).Content.ReadFromJsonAsync<SearchResponse>(JsonOptions);
        var resultB = await (await clientB.GetAsync("/api/search?q=shared-name")).Content.ReadFromJsonAsync<SearchResponse>(JsonOptions);

        resultA!.Projects.Items.Should().ContainSingle(p => p.Id == projectA);
        resultA.Tasks.Items.Should().ContainSingle(t => t.Title == "Shared-Name task A");
        resultB!.Projects.Items.Should().ContainSingle(p => p.Id == projectB);
        resultB.Tasks.Items.Should().ContainSingle(t => t.Title == "Shared-Name task B");

        // Owner "A@example.com" / "B@example.com" from SeedTenantWithOwnerAsync
        // both match "a" trivially — the real assertion that matters is B.
        var usersResultA = await (await clientA.GetAsync("/api/search?q=b@example.com")).Content.ReadFromJsonAsync<SearchResponse>(JsonOptions);
        usersResultA!.Users.Items.Should().BeEmpty();
    }
}
