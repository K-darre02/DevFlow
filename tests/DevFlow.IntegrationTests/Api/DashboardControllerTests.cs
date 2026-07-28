using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevFlow.Api.Contracts.Dashboard;
using DevFlow.Api.Services;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using DevFlow.Infrastructure.Persistence;
using DevFlow.IntegrationTests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DevFlow.IntegrationTests.Api;

// End-to-end through the real pipeline, same rationale as
// ActivityControllerTests: data here comes from calling the real
// Projects/Tasks HTTP endpoints, not from seeding rows directly —
// DashboardServiceTests already covers the aggregation SQL in isolation.
public class DashboardControllerTests : IDisposable
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

    private static async Task CreateTaskAsync(HttpClient client, Guid projectId, string title, string priority = "Medium")
    {
        var response = await client.PostAsJsonAsync("/api/tasks", new { projectId, title, priority }, JsonOptions);
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetDashboard_without_a_token_returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetDashboard_reflects_projects_and_tasks_created_through_the_real_endpoints()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantId, userId) = await SeedTenantWithOwnerAsync(context, "A");
        var client = AuthenticatedClient(tenantId, userId);
        var projectId = await CreateProjectAsync(client, "Project A");
        await CreateTaskAsync(client, projectId, "Task 1", "High");
        await CreateTaskAsync(client, projectId, "Task 2", "Low");

        var response = await client.GetAsync("/api/dashboard");
        var dashboard = await response.Content.ReadFromJsonAsync<DashboardResponse>(JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        dashboard!.ProjectCount.Should().Be(1);
        dashboard.TaskCount.Should().Be(2);
        dashboard.CompletedTaskCount.Should().Be(0);
        dashboard.TasksByPriority[TaskPriority.High].Should().Be(1);
        dashboard.TasksByPriority[TaskPriority.Low].Should().Be(1);
        dashboard.TasksByStatus[TaskItemStatus.Backlog].Should().Be(2);
        // Project creation + both task creations each generated an activity entry.
        dashboard.RecentActivity.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetDashboard_never_reflects_another_tenants_data()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantA, userA) = await SeedTenantWithOwnerAsync(context, "A");
        var (tenantB, userB) = await SeedTenantWithOwnerAsync(context, "B");

        var clientA = AuthenticatedClient(tenantA, userA);
        var projectA = await CreateProjectAsync(clientA, "Project A");
        await CreateTaskAsync(clientA, projectA, "Tenant A task 1");
        await CreateTaskAsync(clientA, projectA, "Tenant A task 2");

        var clientB = AuthenticatedClient(tenantB, userB);
        var projectB = await CreateProjectAsync(clientB, "Project B");
        await CreateTaskAsync(clientB, projectB, "Tenant B task");

        var dashboardA = await (await clientA.GetAsync("/api/dashboard")).Content.ReadFromJsonAsync<DashboardResponse>(JsonOptions);
        var dashboardB = await (await clientB.GetAsync("/api/dashboard")).Content.ReadFromJsonAsync<DashboardResponse>(JsonOptions);

        dashboardA!.ProjectCount.Should().Be(1);
        dashboardA.TaskCount.Should().Be(2);
        dashboardB!.ProjectCount.Should().Be(1);
        dashboardB.TaskCount.Should().Be(1);
    }
}
