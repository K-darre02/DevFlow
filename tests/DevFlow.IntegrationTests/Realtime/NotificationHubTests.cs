using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevFlow.Api.Contracts.Notifications;
using DevFlow.Api.Services;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using DevFlow.Infrastructure.Persistence;
using DevFlow.IntegrationTests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevFlow.IntegrationTests.Realtime;

// Same rationale/harness as TaskHubTests, but proving the *narrower*
// per-user targeting: notification.created must reach only the specific
// user it's for, not the tenant-wide group every other broadcast in this
// system uses — including not reaching another member of the very same
// tenant who happens to also be connected.
public class NotificationHubTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly DevFlowWebApplicationFactory _factory = new();
    private readonly List<HubConnection> _connections = new();

    public void Dispose()
    {
        foreach (var connection in _connections)
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _factory.Dispose();
    }

    private HubConnection BuildConnection(string accessToken)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/tasks"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
            })
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

        _connections.Add(connection);
        return connection;
    }

    private async Task<(Guid TenantId, Guid OwnerId, Guid MemberId, Guid ProjectId)> SeedTenantWithOwnerAndMemberAsync(DevFlowDbContext context)
    {
        var tenant = new Tenant { Name = "Tenant" };
        var owner = new User { Email = "owner@example.com", PasswordHash = "unused" };
        var member = new User { Email = "member@example.com", PasswordHash = "unused" };
        var ownerMembership = new TenantMember { TenantId = tenant.Id, UserId = owner.Id, Role = TenantRole.Owner };
        var memberMembership = new TenantMember { TenantId = tenant.Id, UserId = member.Id, Role = TenantRole.Member };
        var project = new Project { TenantId = tenant.Id, Name = "Project" };

        context.AddRange(tenant, owner, member, ownerMembership, memberMembership, project);
        await context.SaveChangesAsync();

        return (tenant.Id, owner.Id, member.Id, project.Id);
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

    private HttpClient AuthenticatedHttpClient(Guid tenantId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(tenantId, userId));
        return client;
    }

    [Fact]
    public async Task Assigning_a_task_broadcasts_notification_created_only_to_the_assignees_connection()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantId, ownerId, memberId, projectId) = await SeedTenantWithOwnerAndMemberAsync(context);

        var ownerConnection = BuildConnection(GenerateToken(tenantId, ownerId));
        var receivedByOwner = new TaskCompletionSource<NotificationResponse>();
        ownerConnection.On<NotificationResponse>("notification.created", n => receivedByOwner.TrySetResult(n));
        await ownerConnection.StartAsync();

        var memberConnection = BuildConnection(GenerateToken(tenantId, memberId));
        var receivedByMember = new TaskCompletionSource<NotificationResponse>();
        memberConnection.On<NotificationResponse>("notification.created", n => receivedByMember.TrySetResult(n));
        await memberConnection.StartAsync();

        var ownerHttpClient = AuthenticatedHttpClient(tenantId, ownerId);
        var response = await ownerHttpClient.PostAsJsonAsync(
            "/api/tasks", new { projectId, title = "Assigned via realtime", priority = "Medium", assigneeUserId = memberId }, JsonOptions);
        response.EnsureSuccessStatusCode();

        // The assignee's connection receives it...
        var completedForMember = await Task.WhenAny(receivedByMember.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completedForMember.Should().Be(receivedByMember.Task, "the assignee should receive notification.created");
        var notification = await receivedByMember.Task;
        notification.Type.Should().Be(NotificationType.TaskAssigned);

        // ...but the owner's connection — same tenant, and the one who
        // performed the action — never does, even after waiting past the
        // point the assignee already received theirs.
        var completedForOwner = await Task.WhenAny(receivedByOwner.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        completedForOwner.Should().NotBe(receivedByOwner.Task, "a same-tenant connection that isn't the notification's recipient must never receive it");
    }
}
