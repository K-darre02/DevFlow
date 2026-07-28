using DevFlow.Domain.Common;

namespace DevFlow.Domain.Entities;

public class Project : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public Tenant Tenant { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public bool IsArchived { get; set; }

    public ICollection<TaskItem> TaskItems { get; set; } = new List<TaskItem>();
}
