using DevFlow.Domain.Common;

namespace DevFlow.Domain.Entities;

public class TaskAttachment : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public Tenant Tenant { get; set; } = null!;

    public Guid TaskId { get; set; }

    public TaskItem Task { get; set; } = null!;

    public Guid UploadedByUserId { get; set; }

    public User UploadedByUser { get; set; } = null!;

    public string FileName { get; set; } = string.Empty;

    // Storage key, never the original FileName — generated server-side
    // (AttachmentService) as a tenant/task-scoped, GUID-based path, so it's
    // never derived from (and can't be poisoned by) user-supplied input.
    // See IBlobStorageService.
    public string BlobKey { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long Size { get; set; }
}
