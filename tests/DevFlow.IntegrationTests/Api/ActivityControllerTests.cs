using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevFlow.Api.Contracts.Activity;
using DevFlow.Api.Contracts.Common;
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
// TeamAuthorizationTests: activities here are produced by calling the real
// Tasks/Projects/Team HTTP endpoints (which drives the real
// TaskService/ProjectService/TeamService -> MediatR -> ActivityLog handler
// chain), not by seeding ActivityLog rows directly — this is what actually
// proves the feature works end to end, not just that GetActivitiesAsync's
// SQL is correct (ActivityLogServiceTests already covers that in isolation).
public class ActivityControllerTests : IDisposable
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
        var response = await client.PostAsJsonAsync("/api/tasks", new { projectId, title, priority = "Medium" }, JsonOptions);
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetActivities_without_a_token_returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/activity");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetActivities_includes_activity_produced_by_a_real_task_creation()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantId, userId) = await SeedTenantWithOwnerAsync(context, "A");
        var client = AuthenticatedClient(tenantId, userId);
        var projectId = await CreateProjectAsync(client, "Project A");
        await CreateTaskAsync(client, projectId, "Ship the feature");

        var response = await client.GetAsync("/api/activity");
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<ActivityResponse>>(JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Project created + task created, newest (task) first.
        body!.Items.Should().HaveCount(2);
        body.Items[0].ActivityType.Should().Be(ActivityType.TaskCreated);
        body.Items[0].Description.Should().Contain("Ship the feature");
        // "A@example.com" is the persisted user's real email (SeedTenantWithOwnerAsync) —
        // UserEmail is resolved via a DB join, not from the JWT's own claims.
        body.Items[0].UserEmail.Should().Be("A@example.com");
        body.Items[1].ActivityType.Should().Be(ActivityType.ProjectCreated);
    }

    [Fact]
    public async Task GetActivities_never_returns_another_tenants_activity()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantA, userA) = await SeedTenantWithOwnerAsync(context, "A");
        var (tenantB, userB) = await SeedTenantWithOwnerAsync(context, "B");

        var clientA = AuthenticatedClient(tenantA, userA);
        var projectA = await CreateProjectAsync(clientA, "Project A");
        await CreateTaskAsync(clientA, projectA, "Tenant A task");

        var clientB = AuthenticatedClient(tenantB, userB);
        var projectB = await CreateProjectAsync(clientB, "Project B");
        await CreateTaskAsync(clientB, projectB, "Tenant B task");

        var response = await clientA.GetAsync("/api/activity");
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<ActivityResponse>>(JsonOptions);

        body!.Items.Should().OnlyContain(a => a.Description != null && !a.Description.Contains("Tenant B"));
        body.Items.Should().Contain(a => a.Description.Contains("Tenant A task"));
    }

    [Fact]
    public async Task GetActivities_supports_pagination()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantId, userId) = await SeedTenantWithOwnerAsync(context, "A");
        var client = AuthenticatedClient(tenantId, userId);
        var projectId = await CreateProjectAsync(client, "Project A"); // 1 activity
        for (var i = 0; i < 4; i++)
        {
            await CreateTaskAsync(client, projectId, $"Task {i}"); // +4 activities = 5 total
        }

        var pageOne = await (await client.GetAsync("/api/activity?page=1&pageSize=2"))
            .Content.ReadFromJsonAsync<PagedResponse<ActivityResponse>>(JsonOptions);
        var pageTwo = await (await client.GetAsync("/api/activity?page=2&pageSize=2"))
            .Content.ReadFromJsonAsync<PagedResponse<ActivityResponse>>(JsonOptions);

        pageOne!.Items.Should().HaveCount(2);
        pageOne.TotalCount.Should().Be(5);
        pageTwo!.Items.Should().HaveCount(2);
        pageOne.Items.Select(a => a.Id).Should().NotIntersectWith(pageTwo.Items.Select(a => a.Id));
    }

    [Fact]
    public async Task GetActivities_filters_by_entityType()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantId, userId) = await SeedTenantWithOwnerAsync(context, "A");
        var client = AuthenticatedClient(tenantId, userId);
        var projectId = await CreateProjectAsync(client, "Project A");
        await CreateTaskAsync(client, projectId, "A task");

        var response = await client.GetAsync("/api/activity?entityType=Project");
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<ActivityResponse>>(JsonOptions);

        body!.Items.Should().ContainSingle().Which.EntityType.Should().Be(ActivityEntityType.Project);
    }

    [Fact]
    public async Task GetActivities_filters_by_activityType()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantId, userId) = await SeedTenantWithOwnerAsync(context, "A");
        var client = AuthenticatedClient(tenantId, userId);
        var projectId = await CreateProjectAsync(client, "Project A");
        await CreateTaskAsync(client, projectId, "A task");

        var response = await client.GetAsync("/api/activity?activityType=TaskCreated");
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<ActivityResponse>>(JsonOptions);

        body!.Items.Should().ContainSingle().Which.ActivityType.Should().Be(ActivityType.TaskCreated);
    }

    [Fact]
    public async Task GetActivities_filters_by_userId()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantId, userId) = await SeedTenantWithOwnerAsync(context, "A");
        var otherUser = new User { Email = "other@example.com", PasswordHash = "unused" };
        var otherMembership = new TenantMember { TenantId = tenantId, UserId = otherUser.Id, Role = TenantRole.Owner };
        context.AddRange(otherUser, otherMembership);
        await context.SaveChangesAsync();

        var client = AuthenticatedClient(tenantId, userId);
        await CreateProjectAsync(client, "By first user");

        var otherClient = AuthenticatedClient(tenantId, otherUser.Id);
        await CreateProjectAsync(otherClient, "By other user");

        var response = await client.GetAsync($"/api/activity?userId={otherUser.Id}");
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<ActivityResponse>>(JsonOptions);

        body!.Items.Should().ContainSingle().Which.Description.Should().Contain("By other user");
    }
}
