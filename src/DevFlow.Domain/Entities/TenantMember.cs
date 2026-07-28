using DevFlow.Domain.Common;
using DevFlow.Domain.Enums;

namespace DevFlow.Domain.Entities;

// The join between User (global) and Tenant, carrying the user's role
// within that specific tenant. "Joined date" is just CreatedAt (BaseEntity)
// — a separate JoinedAt field would be redundant since a member always joins
// at the moment this row is created (removal is a hard delete, not a status
// flag, so there's no "rejoin an existing row" case that could make the two
// diverge).
public class TenantMember : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public Tenant Tenant { get; set; } = null!;

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public TenantRole Role { get; set; }

    // See ActivityLog.CreatedAtTicks for why this exists — same SQLite
    // ORDER BY-on-DateTimeOffset limitation (AuthController.Login orders a
    // user's memberships by CreatedAt to pick the earliest-joined tenant),
    // same fix.
    public long CreatedAtTicks { get; set; }
}
