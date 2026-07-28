using DevFlow.Domain.Common;

namespace DevFlow.Domain.Entities;

// Simplified for this phase: a User belongs directly to one Tenant.
// The target design (docs/devflow/02-database-design.md) has Users as
// tenant-independent, joined to Tenants via a TenantMemberships table
// (many-to-many with a role) — that lands once auth/membership is built.
public class User : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public Tenant Tenant { get; set; } = null!;

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;
}
