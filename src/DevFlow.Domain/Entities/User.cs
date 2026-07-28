using DevFlow.Domain.Common;

namespace DevFlow.Domain.Entities;

// Tenant-independent, as originally documented (docs/devflow/02-database-design.md):
// one login can hold memberships in multiple tenants via TenantMember. This
// replaces the earlier single-tenant-per-user simplification (User no longer
// implements ITenantOwned, has no TenantId) now that the membership system
// this was always deferred pending — Team Management — actually exists.
public class User : BaseEntity
{
    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public ICollection<TenantMember> TenantMemberships { get; set; } = new List<TenantMember>();
}
