namespace DevFlow.Domain.Common;

/// <summary>
/// Marks an entity as belonging to a single tenant, identified by <see cref="TenantId"/>.
/// Used to apply tenant-scoping consistently in EF Core configuration
/// (docs/devflow/04-security.md §2 — tenant isolation as a structural property).
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
