using DevFlow.Domain.Entities;

namespace DevFlow.Application.Attachments;

public interface IAttachmentService
{
    /// <summary>Throws FluentValidation.ValidationException if the task doesn't resolve within the tenant, the content type isn't allowed, or the size is invalid/over the limit.</summary>
    Task<TaskAttachment> UploadAsync(
        Guid tenantId, Guid uploadedByUserId, UploadAttachmentInput input, Stream content, CancellationToken cancellationToken);

    /// <summary>Throws NotFoundException if the task doesn't exist within the current tenant.</summary>
    Task<IReadOnlyList<TaskAttachment>> GetAttachmentsAsync(Guid taskId, CancellationToken cancellationToken);

    /// <summary>Null if the attachment doesn't exist for this task within the current tenant.</summary>
    Task<Uri?> GetDownloadUrlAsync(Guid taskId, Guid attachmentId, CancellationToken cancellationToken);

    /// <summary>False if the attachment doesn't exist for this task within the current tenant.</summary>
    Task<bool> DeleteAsync(Guid taskId, Guid attachmentId, CancellationToken cancellationToken);
}
