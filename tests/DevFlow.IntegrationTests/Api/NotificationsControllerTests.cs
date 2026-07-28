using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevFlow.Api.Contracts.Common;
using DevFlow.Api.Contracts.Notifications;
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
// ActivityControllerTests: notifications are produced by calling the real
// Tasks HTTP endpoint (driving the real
// TaskService -> MediatR -> TaskAssignedNotificationTriggerHandler chain),
// not by seeding Notification rows directly.
public class NotificationsControllerTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly DevFlowWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<(Guid TenantId, Guid OwnerId, Guid MemberId)> SeedTenantWithOwnerAndMemberAsync(DevFlowDbContext context)
    {
        var tenant = new Tenant { Name = "Tenant" };
        var owner = new User { Email = "owner@example.com", PasswordHash = "unused" };
        var member = new User { Email = "member@example.com", PasswordHash = "unused" };
        var ownerMembership = new TenantMember { TenantId = tenant.Id, UserId = owner.Id, Role = TenantRole.Owner };
        var memberMembership = new TenantMember { TenantId = tenant.Id, UserId = member.Id, Role = TenantRole.Member };
        context.AddRange(tenant, owner, member, ownerMembership, memberMembership);
        await context.SaveChangesAsync();
        return (tenant.Id, owner.Id, member.Id);
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

    private static async Task<Guid> AssignTaskToMemberAsync(HttpClient ownerClient, Guid memberId)
    {
        var projectResponse = await ownerClient.PostAsJsonAsync("/api/projects", new { name = "Project" }, JsonOptions);
        var project = await projectResponse.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = project.GetProperty("id").GetGuid();

        var taskResponse = await ownerClient.PostAsJsonAsync(
            "/api/tasks", new { projectId, title = "Assigned task", priority = "Medium", assigneeUserId = memberId }, JsonOptions);
        var task = await taskResponse.Content.ReadFromJsonAsync<JsonElement>();
        return task.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task GetNotifications_without_a_token_returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/notifications");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Assigning_a_task_notifies_the_assignee_but_not_the_owner_who_assigned_it()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantId, ownerId, memberId) = await SeedTenantWithOwnerAndMemberAsync(context);
        var ownerClient = AuthenticatedClient(tenantId, ownerId);

        await AssignTaskToMemberAsync(ownerClient, memberId);

        var memberClient = AuthenticatedClient(tenantId, memberId);
        var memberNotifications = await (await memberClient.GetAsync("/api/notifications"))
            .Content.ReadFromJsonAsync<PagedResponse<NotificationResponse>>(JsonOptions);

        var ownerNotifications = await (await ownerClient.GetAsync("/api/notifications"))
            .Content.ReadFromJsonAsync<PagedResponse<NotificationResponse>>(JsonOptions);

        // The assignee sees it...
        memberNotifications!.Items.Should().ContainSingle().Which.Type.Should().Be(NotificationType.TaskAssigned);
        // ...but the owner — same tenant, the one who performed the action — does not.
        // This is the security property beyond tenant isolation: notifications
        // are scoped per-user, not just per-tenant.
        ownerNotifications!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUnreadCount_reflects_only_the_current_users_unread_notifications()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantId, ownerId, memberId) = await SeedTenantWithOwnerAndMemberAsync(context);
        var ownerClient = AuthenticatedClient(tenantId, ownerId);
        await AssignTaskToMemberAsync(ownerClient, memberId);

        var memberClient = AuthenticatedClient(tenantId, memberId);
        var memberCount = await (await memberClient.GetAsync("/api/notifications/unread-count"))
            .Content.ReadFromJsonAsync<UnreadCountResponse>(JsonOptions);
        var ownerCount = await (await ownerClient.GetAsync("/api/notifications/unread-count"))
            .Content.ReadFromJsonAsync<UnreadCountResponse>(JsonOptions);

        memberCount!.Count.Should().Be(1);
        ownerCount!.Count.Should().Be(0);
    }

    [Fact]
    public async Task MarkAsRead_marks_the_callers_own_notification_read()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantId, ownerId, memberId) = await SeedTenantWithOwnerAndMemberAsync(context);
        var ownerClient = AuthenticatedClient(tenantId, ownerId);
        await AssignTaskToMemberAsync(ownerClient, memberId);

        var memberClient = AuthenticatedClient(tenantId, memberId);
        var list = await (await memberClient.GetAsync("/api/notifications"))
            .Content.ReadFromJsonAsync<PagedResponse<NotificationResponse>>(JsonOptions);
        var notificationId = list!.Items.Single().Id;

        var response = await memberClient.PatchAsync($"/api/notifications/{notificationId}/read", null);
        var updated = await response.Content.ReadFromJsonAsync<NotificationResponse>(JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        updated!.IsRead.Should().BeTrue();
        (await (await memberClient.GetAsync("/api/notifications/unread-count")).Content.ReadFromJsonAsync<UnreadCountResponse>(JsonOptions))!
            .Count.Should().Be(0);
    }

    [Fact]
    public async Task MarkAsRead_for_another_users_notification_returns_404()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantId, ownerId, memberId) = await SeedTenantWithOwnerAndMemberAsync(context);
        var ownerClient = AuthenticatedClient(tenantId, ownerId);
        await AssignTaskToMemberAsync(ownerClient, memberId);

        var memberClient = AuthenticatedClient(tenantId, memberId);
        var list = await (await memberClient.GetAsync("/api/notifications"))
            .Content.ReadFromJsonAsync<PagedResponse<NotificationResponse>>(JsonOptions);
        var memberNotificationId = list!.Items.Single().Id;

        // The owner (a different user, same tenant) tries to mark the
        // member's notification as read — must not succeed, and must not
        // leak whether it exists (404, same as any other tenant's resource).
        var response = await ownerClient.PatchAsync($"/api/notifications/{memberNotificationId}/read", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MarkAllAsRead_marks_only_the_callers_own_notifications()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantId, ownerId, memberId) = await SeedTenantWithOwnerAndMemberAsync(context);
        var ownerClient = AuthenticatedClient(tenantId, ownerId);
        var projectResponse = await ownerClient.PostAsJsonAsync("/api/projects", new { name = "Project" }, JsonOptions);
        var project = await projectResponse.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = project.GetProperty("id").GetGuid();
        await ownerClient.PostAsJsonAsync("/api/tasks", new { projectId, title = "Task 1", priority = "Medium", assigneeUserId = memberId }, JsonOptions);
        await ownerClient.PostAsJsonAsync("/api/tasks", new { projectId, title = "Task 2", priority = "Medium", assigneeUserId = memberId }, JsonOptions);

        var memberClient = AuthenticatedClient(tenantId, memberId);
        var response = await memberClient.PatchAsync("/api/notifications/read-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await (await memberClient.GetAsync("/api/notifications/unread-count")).Content.ReadFromJsonAsync<UnreadCountResponse>(JsonOptions))!
            .Count.Should().Be(0);
    }
}
