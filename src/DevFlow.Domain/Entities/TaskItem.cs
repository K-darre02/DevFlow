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

    public TaskPriority Priority { get; set; } = TaskPriority.Medium;

    public DateOnly? DueDate { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    // Optimistic concurrency token — see docs/devflow/06-engineering-challenges.md §2.
    // Application-managed (incremented in DevFlowDbContext.SaveChangesAsync on every
    // update), not a byte[] SQL Server "rowversion" and not Postgres' xmin system
    // column either — a plain incrementing counter works identically across every
    // provider (including Sqlite in tests), which a DB-engine-specific mechanism
    // wouldn't. This replaces an earlier byte[] RowVersion property that was a
    // leftover from when this project targeted SQL Server (docs/devflow/05-technical-decisions.md
    // §11) and would not actually have detected conflicts once porous to Postgres.
    public int Version { get; set; } = 1;
}
