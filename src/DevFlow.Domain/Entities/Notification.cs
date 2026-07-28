using DevFlow.Domain.Common;
using DevFlow.Domain.Enums;

namespace DevFlow.Domain.Entities;

// Unlike ActivityLog (a tenant-wide audit trail with an incidental,
// nullable actor), a Notification exists *for* exactly one person —
// UserId is required, and DevFlowDbContext's query filter scopes every
// read by TenantId *and* UserId together, not tenant alone. That's the
// actual security boundary here: "users only see their own notifications"
// isn't a per-endpoint check layered on top, it's structural, the same way
// tenant isolation itself is (docs/devflow/04-security.md §2).
public class Notification : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public Tenant Tenant { get; set; } = null!;

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public NotificationType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public bool IsRead { get; set; }

    // See ActivityLog.CreatedAtTicks for why this exists — same SQLite
    // ORDER BY-on-DateTimeOffset limitation, same fix.
    public long CreatedAtTicks { get; set; }
}
