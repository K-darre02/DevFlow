using DevFlow.Domain.Common;
using DevFlow.Domain.Enums;

namespace DevFlow.Domain.Entities;

public class TaskItem : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public Tenant Tenant { get; set; } = null!;

    public Guid ProjectId { get; set; }

    public Project Project { get; set; } = null!;

    public Guid? AssigneeUserId { get; set; }

    public User? AssigneeUser { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public TaskItemStatus Status { get; set; } = TaskItemStatus.Backlog;

    public DateOnly? DueDate { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    // EF Core concurrency token — see docs/devflow/06-engineering-challenges.md §2
    // (optimistic drag-and-drop conflict detection).
    public byte[]? RowVersion { get; set; }
}
