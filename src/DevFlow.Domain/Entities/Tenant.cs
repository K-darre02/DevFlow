using DevFlow.Domain.Common;

namespace DevFlow.Domain.Entities;

public class Tenant : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public ICollection<TenantMember> Members { get; set; } = new List<TenantMember>();

    public ICollection<Invitation> Invitations { get; set; } = new List<Invitation>();

    public ICollection<Project> Projects { get; set; } = new List<Project>();
}
