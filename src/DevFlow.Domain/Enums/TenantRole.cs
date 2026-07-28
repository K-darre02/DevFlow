namespace DevFlow.Domain.Enums;

// Increasing privilege order (Member < Admin < Owner) — documentation value
// only; authorization checks compare against named roles/policies, not
// ordinals, so this ordering isn't load-bearing.
public enum TenantRole
{
    Member = 0,
    Admin = 1,
    Owner = 2
}
