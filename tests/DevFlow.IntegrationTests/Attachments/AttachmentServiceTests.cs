using System.Text;
using DevFlow.Application.Attachments;
using DevFlow.Application.Common.Exceptions;
using DevFlow.Domain.Entities;
using DevFlow.IntegrationTests.TestSupport;
using FluentAssertions;
using FluentValidation;
using Xunit;

namespace DevFlow.IntegrationTests.Attachments;

public class AttachmentServiceTests : SqliteContextFixture
{
    private readonly IAttachmentService _service;
    private readonly FakeBlobStorageService _blobStorage = new();
    private readonly Tenant _tenant;
    private readonly User _user;
    private readonly TaskItem _task;

    public AttachmentServiceTests()
    {
        _service = new AttachmentService(DbContext, _blobStorage, new UploadAttachmentInputValidator(DbContext));

        _tenant = new Tenant { Name = "Tenant" };
        _user = new User { Email = "uploader@example.com", PasswordHash = "unused" };
        var project = new Project { TenantId = _tenant.Id, Tenant = _tenant, Name = "Project" };
        _task = new TaskItem { TenantId = _tenant.Id, Tenant = _tenant, ProjectId = project.Id, Project = project, Title = "Task" };
        DbContext.AddRange(_tenant, _user, project, _task);
        DbContext.SaveChangesAsync().GetAwaiter().GetResult();

        CurrentUser.TenantId = _tenant.Id;
        CurrentUser.UserId = _user.Id;
    }

    private static Stream ContentStream(string text = "file contents") => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task UploadAsync_persists_the_attachment_and_uploads_the_blob()
    {
        var input = new UploadAttachmentInput(_task.Id, "notes.txt", "text/plain", 13);

        var attachment = await _service.UploadAsync(_tenant.Id, _user.Id, input, ContentStream(), default);

        attachment.Id.Should().NotBe(Guid.Empty);
        attachment.TaskId.Should().Be(_task.Id);
        attachment.UploadedByUserId.Should().Be(_user.Id);
        attachment.FileName.Should().Be("notes.txt");
        attachment.ContentType.Should().Be("text/plain");
        attachment.Size.Should().Be(13);
        attachment.BlobKey.Should().StartWith($"{_tenant.Id}/{_task.Id}/").And.EndWith(".txt");

        _blobStorage.Blobs.Should().ContainKey(attachment.BlobKey);
        Encoding.UTF8.GetString(_blobStorage.Blobs[attachment.BlobKey]).Should().Be("file contents");
    }

    [Fact]
    public async Task UploadAsync_throws_when_the_task_does_not_exist_for_this_tenant()
    {
        var input = new UploadAttachmentInput(Guid.NewGuid(), "notes.txt", "text/plain", 13);

        var act = () => _service.UploadAsync(_tenant.Id, _user.Id, input, ContentStream(), default);

        await act.Should().ThrowAsync<ValidationException>();
        _blobStorage.Blobs.Should().BeEmpty(); // never uploaded — validation failed first
    }

    [Fact]
    public async Task UploadAsync_rejects_a_disallowed_content_type()
    {
        var input = new UploadAttachmentInput(_task.Id, "installer.exe", "application/x-msdownload", 13);

        var act = () => _service.UploadAsync(_tenant.Id, _user.Id, input, ContentStream(), default);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*file type is not allowed*");
        _blobStorage.Blobs.Should().BeEmpty();
    }

    [Fact]
    public async Task UploadAsync_rejects_a_file_over_the_size_limit()
    {
        var input = new UploadAttachmentInput(_task.Id, "huge.pdf", "application/pdf", UploadAttachmentInputValidator.MaxSizeBytes + 1);

        var act = () => _service.UploadAsync(_tenant.Id, _user.Id, input, ContentStream(), default);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*size limit*");
        _blobStorage.Blobs.Should().BeEmpty();
    }

    [Fact]
    public async Task UploadAsync_rejects_an_empty_file()
    {
        var input = new UploadAttachmentInput(_task.Id, "empty.txt", "text/plain", 0);

        var act = () => _service.UploadAsync(_tenant.Id, _user.Id, input, ContentStream(), default);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*empty*");
        _blobStorage.Blobs.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAttachmentsAsync_returns_the_tasks_attachments()
    {
        await _service.UploadAsync(_tenant.Id, _user.Id, new UploadAttachmentInput(_task.Id, "a.txt", "text/plain", 5), ContentStream("aaaaa"), default);
        await _service.UploadAsync(_tenant.Id, _user.Id, new UploadAttachmentInput(_task.Id, "b.txt", "text/plain", 5), ContentStream("bbbbb"), default);

        var attachments = await _service.GetAttachmentsAsync(_task.Id, default);

        attachments.Should().HaveCount(2);
        attachments.Select(a => a.FileName).Should().Contain(new[] { "a.txt", "b.txt" });
    }

    [Fact]
    public async Task GetAttachmentsAsync_does_not_return_attachments_belonging_to_a_different_task()
    {
        var otherTask = new TaskItem { TenantId = _tenant.Id, Tenant = _tenant, ProjectId = _task.ProjectId, Title = "Other task" };
        DbContext.TaskItems.Add(otherTask);
        await DbContext.SaveChangesAsync();
        await _service.UploadAsync(_tenant.Id, _user.Id, new UploadAttachmentInput(otherTask.Id, "other.txt", "text/plain", 5), ContentStream("other"), default);

        var attachments = await _service.GetAttachmentsAsync(_task.Id, default);

        attachments.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAttachmentsAsync_throws_NotFoundException_for_a_task_that_does_not_exist()
    {
        var act = () => _service.GetAttachmentsAsync(Guid.NewGuid(), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetDownloadUrlAsync_returns_a_url_for_an_existing_attachment()
    {
        var attachment = await _service.UploadAsync(_tenant.Id, _user.Id, new UploadAttachmentInput(_task.Id, "a.txt", "text/plain", 5), ContentStream("aaaaa"), default);

        var url = await _service.GetDownloadUrlAsync(_task.Id, attachment.Id, default);

        url.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDownloadUrlAsync_returns_null_for_an_attachment_that_does_not_exist()
    {
        var url = await _service.GetDownloadUrlAsync(_task.Id, Guid.NewGuid(), default);

        url.Should().BeNull();
    }

    [Fact]
    public async Task GetDownloadUrlAsync_returns_null_when_the_attachment_belongs_to_a_different_task()
    {
        var otherTask = new TaskItem { TenantId = _tenant.Id, Tenant = _tenant, ProjectId = _task.ProjectId, Title = "Other task" };
        DbContext.TaskItems.Add(otherTask);
        await DbContext.SaveChangesAsync();
        var attachment = await _service.UploadAsync(_tenant.Id, _user.Id, new UploadAttachmentInput(otherTask.Id, "other.txt", "text/plain", 5), ContentStream("other"), default);

        // Requesting it through the *wrong* task id — "verify task access".
        var url = await _service.GetDownloadUrlAsync(_task.Id, attachment.Id, default);

        url.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_removes_the_row_and_the_blob()
    {
        var attachment = await _service.UploadAsync(_tenant.Id, _user.Id, new UploadAttachmentInput(_task.Id, "a.txt", "text/plain", 5), ContentStream("aaaaa"), default);

        var deleted = await _service.DeleteAsync(_task.Id, attachment.Id, default);

        deleted.Should().BeTrue();
        (await _service.GetAttachmentsAsync(_task.Id, default)).Should().BeEmpty();
        _blobStorage.DeletedKeys.Should().Contain(attachment.BlobKey);
    }

    [Fact]
    public async Task DeleteAsync_returns_false_for_an_attachment_that_does_not_exist()
    {
        var deleted = await _service.DeleteAsync(_task.Id, Guid.NewGuid(), default);

        deleted.Should().BeFalse();
    }

    [Fact]
    public async Task Attachments_are_not_visible_to_another_tenant()
    {
        var attachment = await _service.UploadAsync(_tenant.Id, _user.Id, new UploadAttachmentInput(_task.Id, "a.txt", "text/plain", 5), ContentStream("aaaaa"), default);

        var otherTenant = new Tenant { Name = "Other Tenant" };
        DbContext.Tenants.Add(otherTenant);
        await DbContext.SaveChangesAsync();
        CurrentUser.TenantId = otherTenant.Id;

        var url = await _service.GetDownloadUrlAsync(_task.Id, attachment.Id, default);
        var deleted = await _service.DeleteAsync(_task.Id, attachment.Id, default);

        url.Should().BeNull();
        deleted.Should().BeFalse();
    }
}
