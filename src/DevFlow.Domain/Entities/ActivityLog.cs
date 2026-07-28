using DevFlow.Domain.Common;
using DevFlow.Domain.Enums;

namespace DevFlow.Domain.Entities;

// Append-only: nothing in this system ever updates or deletes a row here.
// Still inherits BaseEntity (rather than a bespoke Id/CreatedAt pair) for
// consistency with every other entity — UpdatedAt is simply never touched
// again after the initial insert.
public class ActivityLog : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public Tenant Tenant { get; set; } = null!;

    // Nullable: not every activity necessarily has a human actor (none do
    // today — every current activity type is triggered by an authenticated
    // request — but the column doesn't assume that stays true, e.g. a future
    // system-initiated cleanup job).
    public Guid? UserId { get; set; }

    public User? User { get; set; }

    public ActivityType ActivityType { get; set; }

    public ActivityEntityType EntityType { get; set; }

    public Guid EntityId { get; set; }

    public string Description { get; set; } = string.Empty;

    // A plain long proxy for CreatedAt, auto-populated in
    // DevFlowDbContext.SaveChangesAsync the same way TaskItem.Version is —
    // exists purely so pagination can ORDER BY it: SQLite's EF Core provider
    // (used by this solution's tests) can translate ordering by a plain
    // integer column but not by a DateTimeOffset column, or even
    // DateTimeOffset.Ticks computed inline — both fail the same way
    // GetMembersAsync's ORDER BY once did, except that case could fall back
    // to sorting a small already-materialized list in memory, and a
    // paginated query genuinely can't (Skip/Take without a server-side
    // ORDER BY isn't reliably deterministic across pages). CreatedAt itself
    // remains the display-facing timestamp.
    public long CreatedAtTicks { get; set; }

    // Serialized JSON (System.Text.Json), stored as plain text rather than
    // a provider-specific jsonb column type — see InviteMemberInputValidator
    // and TeamService.GetMembersAsync for the established precedent of
    // avoiding anything that only one of Npgsql/Sqlite supports, since Sqlite
    // is what this solution's tests run against. Never queried by structure,
    // only stored and displayed, so plain text costs nothing here.
    public string? Metadata { get; set; }
}
