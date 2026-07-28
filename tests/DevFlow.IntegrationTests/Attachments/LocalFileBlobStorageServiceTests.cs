using System.Text;
using DevFlow.Api.Storage;
using DevFlow.Application.Common;
using DevFlow.IntegrationTests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevFlow.IntegrationTests.Attachments;

public class LocalFileBlobStorageServiceTests : IDisposable
{
    private const string SigningKey = "test-signing-key-at-least-32-bytes-long";

    private readonly string _rootPath;
    private readonly LocalFileBlobStorageService _service;

    public LocalFileBlobStorageServiceTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "devflow-attachment-tests", Guid.NewGuid().ToString());
        var environment = new FakeWebHostEnvironment { ContentRootPath = _rootPath };
        var options = Options.Create(new LocalFileStorageOptions { RootPath = "attachments", SigningKey = SigningKey });

        var httpContext = new DefaultHttpContext
        {
            Request = { Scheme = "https", Host = new HostString("localhost", 5001) }
        };
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

        _service = new LocalFileBlobStorageService(options, environment, httpContextAccessor);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task UploadAsync_then_OpenReadAsync_round_trips_the_content()
    {
        var content = Encoding.UTF8.GetBytes("hello world");

        await _service.UploadAsync("tenant/task/blob.txt", new MemoryStream(content), "text/plain", default);

        await using var stream = await _service.OpenReadAsync("tenant/task/blob.txt", default);
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync();

        text.Should().Be("hello world");
    }

    [Fact]
    public async Task OpenReadAsync_throws_for_a_blob_that_was_never_uploaded()
    {
        var act = () => _service.OpenReadAsync("tenant/task/never-uploaded.txt", default);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task DeleteAsync_removes_the_file()
    {
        await _service.UploadAsync("tenant/task/blob.txt", new MemoryStream(Encoding.UTF8.GetBytes("data")), "text/plain", default);

        await _service.DeleteAsync("tenant/task/blob.txt", default);

        var act = () => _service.OpenReadAsync("tenant/task/blob.txt", default);
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task DeleteAsync_for_a_blob_that_does_not_exist_does_not_throw()
    {
        var act = () => _service.DeleteAsync("tenant/task/does-not-exist.txt", default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetDownloadUrlAsync_returns_a_signed_url_pointing_at_the_passthrough_endpoint()
    {
        var url = await _service.GetDownloadUrlAsync("tenant/task/blob.txt", "report.pdf", "application/pdf", default);

        url.Scheme.Should().Be("https");
        url.Host.Should().Be("localhost");
        url.AbsolutePath.Should().Be("/api/blob-downloads");

        var query = QueryHelpers.ParseQuery(url.Query);
        query["blobKey"].ToString().Should().Be("tenant/task/blob.txt");
        query["fileName"].ToString().Should().Be("report.pdf");
        query["contentType"].ToString().Should().Be("application/pdf");
        query.Should().ContainKey("expires");
        query.Should().ContainKey("sig");
    }

    [Fact]
    public async Task GetDownloadUrlAsync_produces_a_url_whose_signature_verifies_as_valid()
    {
        var url = await _service.GetDownloadUrlAsync("tenant/task/blob.txt", "report.pdf", "application/pdf", default);
        var query = QueryHelpers.ParseQuery(url.Query);

        var isValid = SignedBlobUrl.Verify(
            SigningKey, "tenant/task/blob.txt", "report.pdf", "application/pdf",
            long.Parse(query["expires"]!), query["sig"]!);

        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_rejects_a_blob_key_containing_path_traversal()
    {
        var act = () => _service.UploadAsync("../../etc/passwd", new MemoryStream(), "text/plain", default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
