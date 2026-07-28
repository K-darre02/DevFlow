using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevFlow.Api.Contracts.Tasks;
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

// End-to-end through the real hub: a genuine HubConnection talking to
// TaskHub over the WebApplicationFactory's in-memory TestServer. TestServer
// doesn't implement real WebSockets, so the client is forced onto
// LongPolling — a documented ASP.NET Core testing limitation, not a
// weakening of what's under test (the same [Authorize]/group-membership
// logic runs regardless of transport).
public class TaskHubTests : IDisposable
{
    // Matches DependencyInjection.AddJsonOptions (JsonStringEnumConverter) —
    // needed here for the plain REST calls this file makes to seed/read task
    // state through HttpClient, separate from the hub's own JSON protocol.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly DevFlowWebApplicationFactory _factory = new();
    private readonly List<HubConnection> _connections = new();

    // Plain IDisposable rather than xUnit's IAsyncLifetime: HubConnection's
    // DisposeAsync has nothing meaningful to await here beyond "stop trying
    // to talk to a TestServer that's about to go away" — blocking on it
    // synchronously during teardown is simpler than adding async lifecycle
    // just for cleanup.
    public void Dispose()
    {
        foreach (var connection in _connections)
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _factory.Dispose();
    }

    private HubConnection BuildConnection(string? accessToken)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/tasks"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (accessToken is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                }
            })
            // Must match DependencyInjection.AddApiServices' AddJsonProtocol
            // exactly — otherwise this client silently fails to deserialize
            // incoming messages (enums arrive as strings, not the ints this
            // client's default JSON options expect) and every event handler
            // just never fires. See the comment there.
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

        _connections.Add(connection);
        return connection;
    }

    private async Task<(Guid TenantId, Guid UserId, Guid ProjectId)> SeedTenantWithProjectAsync(DevFlowDbContext context, string label)
    {
        var tenant = new Tenant { Name = $"Tenant {label}" };
        var user = new User { Email = $"{label}@example.com", PasswordHash = "unused" };
        var membership = new TenantMember { TenantId = tenant.Id, UserId = user.Id, Role = TenantRole.Owner };
        var project = new Project { TenantId = tenant.Id, Name = $"Project {label}" };

        context.AddRange(tenant, user, membership, project);
        await context.SaveChangesAsync();

        return (tenant.Id, user.Id, project.Id);
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

    private HttpClient AuthenticatedHttpClient(Guid tenantId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(tenantId, userId));
        return client;
    }

    [Fact]
    public async Task Connecting_without_a_token_is_rejected()
    {
        var connection = BuildConnection(accessToken: null);

        var act = () => connection.StartAsync();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Connecting_with_a_garbage_token_is_rejected()
    {
        var connection = BuildConnection(accessToken: "not-a-real-jwt");

        var act = () => connection.StartAsync();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Connecting_with_a_valid_token_succeeds_and_creating_a_task_broadcasts_task_created()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantId, userId, projectId) = await SeedTenantWithProjectAsync(context, "A");

        var connection = BuildConnection(GenerateToken(tenantId, userId));
        var received = new TaskCompletionSource<TaskResponse>();
        connection.On<TaskResponse>("task.created", task => received.TrySetResult(task));

        await connection.StartAsync();
        connection.State.Should().Be(HubConnectionState.Connected);

        var httpClient = AuthenticatedHttpClient(tenantId, userId);
        var response = await httpClient.PostAsJsonAsync("/api/tasks", new { projectId, title = "Realtime task", priority = "Medium" });
        response.EnsureSuccessStatusCode();

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.Should().Be(received.Task, "the task.created event should have arrived within the timeout");
        var broadcastTask = await received.Task;
        broadcastTask.Title.Should().Be("Realtime task");
        broadcastTask.ProjectId.Should().Be(projectId);
    }

    [Fact]
    public async Task Moving_a_task_broadcasts_task_moved_with_the_new_and_previous_status()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantId, userId, projectId) = await SeedTenantWithProjectAsync(context, "A");
        var httpClient = AuthenticatedHttpClient(tenantId, userId);
        var createResponse = await httpClient.PostAsJsonAsync("/api/tasks", new { projectId, title = "Move me", priority = "Medium" });
        var created = await createResponse.Content.ReadFromJsonAsync<TaskResponse>(JsonOptions);

        var connection = BuildConnection(GenerateToken(tenantId, userId));
        var received = new TaskCompletionSource<(TaskResponse Task, TaskItemStatus FromStatus)>();
        connection.On<TaskResponse, TaskItemStatus>("task.moved", (task, fromStatus) => received.TrySetResult((task, fromStatus)));
        await connection.StartAsync();

        var updateRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/tasks/{created!.Id}")
        {
            Content = JsonContent.Create(new { status = "InProgress" })
        };
        updateRequest.Headers.Add("If-Match", $"\"{created.Version}\""); // ETag format — TasksController.UpdateTask trims the quotes
        var updateResponse = await httpClient.SendAsync(updateRequest);
        updateResponse.EnsureSuccessStatusCode();

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.Should().Be(received.Task, "the task.moved event should have arrived within the timeout");
        var (movedTask, fromStatus) = await received.Task;
        movedTask.Status.Should().Be(TaskItemStatus.InProgress);
        fromStatus.Should().Be(TaskItemStatus.Backlog);
    }

    [Fact]
    public async Task A_tenants_task_events_are_never_received_by_a_connection_from_another_tenant()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantA, userA, projectA) = await SeedTenantWithProjectAsync(context, "A");
        var (tenantB, userB, _) = await SeedTenantWithProjectAsync(context, "B");

        var connectionB = BuildConnection(GenerateToken(tenantB, userB));
        var receivedByB = new TaskCompletionSource<TaskResponse>();
        connectionB.On<TaskResponse>("task.created", task => receivedByB.TrySetResult(task));
        await connectionB.StartAsync();

        var connectionA = BuildConnection(GenerateToken(tenantA, userA));
        var receivedByA = new TaskCompletionSource<TaskResponse>();
        connectionA.On<TaskResponse>("task.created", task => receivedByA.TrySetResult(task));
        await connectionA.StartAsync();

        var httpClient = AuthenticatedHttpClient(tenantA, userA);
        var response = await httpClient.PostAsJsonAsync("/api/tasks", new { projectId = projectA, title = "Tenant A only", priority = "Medium" });
        response.EnsureSuccessStatusCode();

        // A (same tenant as the mutation) must receive it...
        var completedForA = await Task.WhenAny(receivedByA.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completedForA.Should().Be(receivedByA.Task);

        // ...B (a different tenant) must not, even after waiting past the
        // point A already received its event — proves absence, not just a
        // race where B's handler hadn't run yet.
        var completedForB = await Task.WhenAny(receivedByB.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        completedForB.Should().NotBe(receivedByB.Task, "a connection from another tenant must never receive this event");
    }
}
