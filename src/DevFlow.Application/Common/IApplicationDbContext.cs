using DevFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevFlow.Application.Common;

/// <summary>
/// The persistence seam Application services depend on, implemented by
/// DevFlowDbContext (Infrastructure). Keeps EF Core's provider-specific
/// packages (Npgsql, etc.) out of Application while still allowing services
/// to write real, composable LINQ queries — the common, pragmatic middle
/// ground between a hand-rolled repository per aggregate and Application
/// depending on Infrastructure directly. Tenant scoping is inherited for
/// free: whatever concrete DbContext is registered against this interface
/// applies its own global query filters (see DevFlowDbContext).
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Tenant> Tenants { get; }

    DbSet<User> Users { get; }

    DbSet<Project> Projects { get; }

    DbSet<TaskItem> TaskItems { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
