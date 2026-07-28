using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevFlow.Api.Contracts.Attachments;
using DevFlow.Api.Services;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using DevFlow.Infrastructure.Persistence;
using DevFlow.IntegrationTests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DevFlow.IntegrationTests.Api;

// End-to-end through the real pipeline, including a real HTTP round trip
// through the local-storage signed-URL passthrough endpoint
// (BlobDownloadsController) — Download() below doesn't just check for a
// 302, it lets HttpClient follow the redirect and asserts on the actual
// bytes that come back, which is what actually proves the whole upload ->
// store -> sign -> verify -> stream chain works together.
public class TaskAttachmentsControllerTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly DevFlowWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<(Guid TenantId, Guid UserId, Guid TaskId)> SeedTenantWithTaskAsync(DevFlowDbContext context, string label)
    {
        var tenant = new Tenant { Name = $"Tenant {label}" };
        var user = new User { Email = $"{label}@example.com", PasswordHash = "unused" };
        var membership = new TenantMember { TenantId = tenant.Id, UserId = user.Id, Role = TenantRole.Owner };
        var project = new Project { TenantId = tenant.Id, Name = $"Project {label}" };
        var task = new TaskItem { TenantId = tenant.Id, ProjectId = project.Id, Title = $"Task {label}" };

        context.AddRange(tenant, user, membership, project, task);
        await context.SaveChangesAsync();

        return (tenant.Id, user.Id, task.Id);
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

    private static MultipartFormDataContent BuildUpload(string fileName, string contentType, byte[] bytes)
    {
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { fileContent, "file", fileName } };
    }

    [Fact]
    public async Task Upload_without_a_token_returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync($"/api/tasks/{Guid.NewGuid()}/attachments", BuildUpload("a.txt", "text/plain", "hi"u8.ToArray()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Upload_a_valid_file_persists_it_and_returns_metadata()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantId, userId, taskId) = await SeedTenantWithTaskAsync(context, "A");
        var client = AuthenticatedClient(tenantId, userId);

        var response = await client.PostAsync($"/api/tasks/{taskId}/attachments", BuildUpload("notes.txt", "text/plain", "hello"u8.ToArray()));
        var attachment = await response.Content.ReadFromJsonAsync<AttachmentResponse>(JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        attachment!.FileName.Should().Be("notes.txt");
        attachment.ContentType.Should().Be("text/plain");
        attachment.Size.Should().Be(5);
        attachment.TaskId.Should().Be(taskId);
    }

    [Fact]
    public async Task Upload_rejects_a_disallowed_content_type()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantId, userId, taskId) = await SeedTenantWithTaskAsync(context, "A");
        var client = AuthenticatedClient(tenantId, userId);

        var response = await client.PostAsync(
            $"/api/tasks/{taskId}/attachments", BuildUpload("installer.exe", "application/x-msdownload", "MZ"u8.ToArray()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_rejects_a_file_over_the_size_limit()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantId, userId, taskId) = await SeedTenantWithTaskAsync(context, "A");
        var client = AuthenticatedClient(tenantId, userId);
        var oversized = new byte[11 * 1024 * 1024]; // 11 MB > the 10 MB limit

        var response = await client.PostAsync($"/api/tasks/{taskId}/attachments", BuildUpload("huge.pdf", "application/pdf", oversized));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_for_another_tenants_task_is_rejected()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantA, userA, _) = await SeedTenantWithTaskAsync(context, "A");
        var (_, _, taskB) = await SeedTenantWithTaskAsync(context, "B");
        var clientA = AuthenticatedClient(tenantA, userA);

        // tenantA's token, targeting tenantB's task id.
        var response = await clientA.PostAsync($"/api/tasks/{taskB}/attachments", BuildUpload("a.txt", "text/plain", "hi"u8.ToArray()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest); // UploadAttachmentInputValidator's tenant-scoped TaskId existence check
    }

    [Fact]
    public async Task GetAttachments_lists_uploaded_attachments()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantId, userId, taskId) = await SeedTenantWithTaskAsync(context, "A");
        var client = AuthenticatedClient(tenantId, userId);
        await client.PostAsync($"/api/tasks/{taskId}/attachments", BuildUpload("a.txt", "text/plain", "hi"u8.ToArray()));

        var response = await client.GetAsync($"/api/tasks/{taskId}/attachments");
        var attachments = await response.Content.ReadFromJsonAsync<List<AttachmentResponse>>(JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        attachments.Should().ContainSingle().Which.FileName.Should().Be("a.txt");
        attachments![0].UploadedByEmail.Should().Be("A@example.com");
    }

    [Fact]
    public async Task GetAttachments_for_another_tenants_task_returns_404()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantA, userA, _) = await SeedTenantWithTaskAsync(context, "A");
        var (_, _, taskB) = await SeedTenantWithTaskAsync(context, "B");
        var clientA = AuthenticatedClient(tenantA, userA);

        var response = await clientA.GetAsync($"/api/tasks/{taskB}/attachments");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Download_redirects_through_the_signed_url_and_returns_the_original_bytes()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantId, userId, taskId) = await SeedTenantWithTaskAsync(context, "A");
        var client = AuthenticatedClient(tenantId, userId);
        var uploadResponse = await client.PostAsync($"/api/tasks/{taskId}/attachments", BuildUpload("report.txt", "text/plain", "the report contents"u8.ToArray()));
        var attachment = await uploadResponse.Content.ReadFromJsonAsync<AttachmentResponse>(JsonOptions);

        // HttpClient follows the 302 to /api/blob-downloads automatically
        // (WebApplicationFactoryClientOptions.AllowAutoRedirect defaults to
        // true) — this is the actual signed-URL round trip, not a mock of it.
        var response = await client.GetAsync($"/api/tasks/{taskId}/attachments/{attachment!.Id}/download");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Encoding.UTF8.GetString(bytes).Should().Be("the report contents");
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
    }

    [Fact]
    public async Task Download_for_another_tenants_attachment_returns_404()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantA, userA, taskA) = await SeedTenantWithTaskAsync(context, "A");
        var (tenantB, userB, taskB) = await SeedTenantWithTaskAsync(context, "B");
        var clientB = AuthenticatedClient(tenantB, userB);
        var uploadResponse = await clientB.PostAsync($"/api/tasks/{taskB}/attachments", BuildUpload("b.txt", "text/plain", "b"u8.ToArray()));
        var attachment = await uploadResponse.Content.ReadFromJsonAsync<AttachmentResponse>(JsonOptions);

        var clientA = AuthenticatedClient(tenantA, userA);
        var response = await clientA.GetAsync($"/api/tasks/{taskB}/attachments/{attachment!.Id}/download");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_removes_the_attachment()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantId, userId, taskId) = await SeedTenantWithTaskAsync(context, "A");
        var client = AuthenticatedClient(tenantId, userId);
        var uploadResponse = await client.PostAsync($"/api/tasks/{taskId}/attachments", BuildUpload("a.txt", "text/plain", "hi"u8.ToArray()));
        var attachment = await uploadResponse.Content.ReadFromJsonAsync<AttachmentResponse>(JsonOptions);

        var deleteResponse = await client.DeleteAsync($"/api/tasks/{taskId}/attachments/{attachment!.Id}");
        var listResponse = await client.GetAsync($"/api/tasks/{taskId}/attachments");
        var remaining = await listResponse.Content.ReadFromJsonAsync<List<AttachmentResponse>>(JsonOptions);

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_for_another_tenants_attachment_returns_404()
    {
        var context = await _factory.CreateDbContextAsync();
        var (tenantA, userA, taskA) = await SeedTenantWithTaskAsync(context, "A");
        var (tenantB, userB, taskB) = await SeedTenantWithTaskAsync(context, "B");
        var clientB = AuthenticatedClient(tenantB, userB);
        var uploadResponse = await clientB.PostAsync($"/api/tasks/{taskB}/attachments", BuildUpload("b.txt", "text/plain", "b"u8.ToArray()));
        var attachment = await uploadResponse.Content.ReadFromJsonAsync<AttachmentResponse>(JsonOptions);

        var clientA = AuthenticatedClient(tenantA, userA);
        var response = await clientA.DeleteAsync($"/api/tasks/{taskB}/attachments/{attachment!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
