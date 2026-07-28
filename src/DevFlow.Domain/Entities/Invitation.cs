using DevFlow.Domain.Common;
using DevFlow.Domain.Enums;

namespace DevFlow.Domain.Entities;

// TokenHash, never the raw token — same reasoning as the documented
// RefreshTokens design (docs/devflow/02-database-design.md#table-notes):
// a database read alone shouldn't hand out something usable to accept the
// invitation. The raw token is returned to the inviter exactly once, at
// creation time (TeamController), and never persisted.
public class Invitation : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public Tenant Tenant { get; set; } = null!;

    public string Email { get; set; } = string.Empty;

    public TenantRole Role { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    public Guid InvitedByUserId { get; set; }

    public User InvitedByUser { get; set; } = null!;
}
