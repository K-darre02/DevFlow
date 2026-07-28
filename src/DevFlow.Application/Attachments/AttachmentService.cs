using DevFlow.Application.Common;
using DevFlow.Application.Common.Exceptions;
using DevFlow.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DevFlow.Application.Attachments;

public class AttachmentService : IAttachmentService
{
    private readonly IApplicationDbContext _context;
    private readonly IBlobStorageService _blobStorage;
    private readonly IValidator<UploadAttachmentInput> _uploadValidator;

    public AttachmentService(IApplicationDbContext context, IBlobStorageService blobStorage, IValidator<UploadAttachmentInput> uploadValidator)
    {
        _context = context;
        _blobStorage = blobStorage;
        _uploadValidator = uploadValidator;
    }

    public async Task<TaskAttachment> UploadAsync(
        Guid tenantId, Guid uploadedByUserId, UploadAttachmentInput input, Stream content, CancellationToken cancellationToken)
    {
        await _uploadValidator.ValidateAndThrowAsync(input, cancellationToken);

        // Tenant/task/random-scoped, never derived from the user-supplied
        // file name — see TaskAttachment.BlobKey.
        var blobKey = $"{tenantId}/{input.TaskId}/{Guid.NewGuid()}{SafeExtension(input.FileName)}";

        await _blobStorage.UploadAsync(blobKey, content, input.ContentType, cancellationToken);

        var attachment = new TaskAttachment
        {
            TenantId = tenantId,
            TaskId = input.TaskId,
            UploadedByUserId = uploadedByUserId,
            FileName = input.FileName,
            BlobKey = blobKey,
            ContentType = input.ContentType,
            Size = input.Size
        };

        _context.TaskAttachments.Add(attachment);
        await _context.SaveChangesAsync(cancellationToken);

        return attachment;
    }

    public async Task<IReadOnlyList<TaskAttachment>> GetAttachmentsAsync(Guid taskId, CancellationToken cancellationToken)
    {
        await EnsureTaskExistsAsync(taskId, cancellationToken);

        // Ordered client-side, same reasoning as TeamService.GetMembersAsync:
        // SQLite's EF Core provider can't translate ORDER BY on
        // DateTimeOffset, and a task's attachment list is small enough that
        // sorting after materializing costs nothing.
        var attachments = await _context.TaskAttachments
            .AsNoTracking()
            .Include(a => a.UploadedByUser)
            .Where(a => a.TaskId == taskId)
            .ToListAsync(cancellationToken);

        return attachments.OrderBy(a => a.CreatedAt).ToList();
    }

    public async Task<Uri?> GetDownloadUrlAsync(Guid taskId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var attachment = await FindAttachmentAsync(taskId, attachmentId, cancellationToken);

        if (attachment is null)
        {
            return null;
        }

        return await _blobStorage.GetDownloadUrlAsync(attachment.BlobKey, attachment.FileName, attachment.ContentType, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid taskId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var attachment = await FindAttachmentAsync(taskId, attachmentId, cancellationToken);

        if (attachment is null)
        {
            return false;
        }

        // Blob deleted before the row: if this throws, the row (and the
        // caller's ability to retry) survives, rather than losing track of
        // a blob that's still sitting in storage.
        await _blobStorage.DeleteAsync(attachment.BlobKey, cancellationToken);

        _context.TaskAttachments.Remove(attachment);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    private async Task<TaskAttachment?> FindAttachmentAsync(Guid taskId, Guid attachmentId, CancellationToken cancellationToken)
    {
        // TenantId scoping comes from the ambient query filter; TaskId is
        // checked explicitly so an attachment id that's valid but belongs to
        // a *different* task in the same tenant isn't treated as found —
        // "verify task access" from the download flow's own requirements.
        return await _context.TaskAttachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.TaskId == taskId, cancellationToken);
    }

    private async Task EnsureTaskExistsAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var exists = await _context.TaskItems.AnyAsync(t => t.Id == taskId, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException(nameof(TaskItem), taskId);
        }
    }

    // Keeps only a short, alphanumeric extension from the original file
    // name (e.g. ".pdf") — never any other part of the caller-supplied
    // name, which is what BlobKey is built from and must never be
    // influenced by user input beyond this.
    private static string SafeExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);

        if (string.IsNullOrEmpty(extension) || extension.Length > 10 || !extension.Skip(1).All(char.IsLetterOrDigit))
        {
            return string.Empty;
        }

        return extension;
    }
}
