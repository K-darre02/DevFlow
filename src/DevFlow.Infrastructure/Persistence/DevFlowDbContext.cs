using DevFlow.Application.Common;
using DevFlow.Domain.Common;
using DevFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevFlow.Infrastructure.Persistence;

public class DevFlowDbContext : DbContext, IApplicationDbContext
{
    private readonly ICurrentUserService _currentUserService;

    public DevFlowDbContext(DbContextOptions<DevFlowDbContext> options, ICurrentUserService currentUserService)
        : base(options)
    {
        _currentUserService = currentUserService;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<User> Users => Set<User>();

    public DbSet<TenantMember> TenantMembers => Set<TenantMember>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<TaskItem> TaskItems => Set<TaskItem>();

    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<TaskAttachment> TaskAttachments => Set<TaskAttachment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DevFlowDbContext).Assembly);

        // Tenant isolation as a structural property (docs/devflow/04-security.md §2),
        // not a per-query convention. IMPORTANT: these lambdas reference
        // `_currentUserService.TenantId` directly (an instance field access), not a
        // local variable captured from a snapshot — OnModelCreating runs once per
        // DbContext *type* (the compiled model is cached), not once per request, so
        // capturing `_currentUserService.TenantId` into a local first would bake
        // whichever tenant happened to build the model first into every future query,
        // for every user, permanently. Referencing the field itself lets EF Core
        // re-evaluate it per query against this context instance.
        //
        // User has no filter here — it's intentionally global (see User.cs);
        // TenantMember is what carries tenant scoping for user-related data now.
        modelBuilder.Entity<TenantMember>().HasQueryFilter(m => m.TenantId == _currentUserService.TenantId);
        modelBuilder.Entity<Invitation>().HasQueryFilter(i => i.TenantId == _currentUserService.TenantId);
        modelBuilder.Entity<Project>().HasQueryFilter(p => p.TenantId == _currentUserService.TenantId);
        modelBuilder.Entity<TaskItem>().HasQueryFilter(t => t.TenantId == _currentUserService.TenantId);
        modelBuilder.Entity<ActivityLog>().HasQueryFilter(a => a.TenantId == _currentUserService.TenantId);

        // Notification is the one entity in this system scoped by *both*
        // tenant and user — not tenant-wide like everything above. "Users
        // only see their own notifications" is enforced here structurally,
        // the same way tenant isolation itself is, rather than as a filter
        // callers have to remember to add.
        modelBuilder.Entity<Notification>().HasQueryFilter(
            n => n.TenantId == _currentUserService.TenantId && n.UserId == _currentUserService.UserId);

        modelBuilder.Entity<TaskAttachment>().HasQueryFilter(a => a.TenantId == _currentUserService.TenantId);

        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        // TaskItem.Version is the optimistic concurrency token (see
        // TaskItem.cs). Nothing generates it automatically — this is that
        // generation, applied to every modified task on every save so the
        // token changes whenever the row does, regardless of which fields
        // changed. A caller that wants to *check* a client-supplied expected
        // version sets Entry(task).Property(t => t.Version).OriginalValue
        // before calling SaveChangesAsync; that's independent of the
        // CurrentValue bump happening here.
        foreach (var entry in ChangeTracker.Entries<TaskItem>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.Version++;
            }
        }

        // See ActivityLog.CreatedAtTicks — a plain long proxy for CreatedAt
        // that pagination can ORDER BY (SQLite's provider can't translate
        // ordering by CreatedAt itself). Set from the same `now` as
        // CreatedAt above so the two never disagree.
        foreach (var entry in ChangeTracker.Entries<ActivityLog>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtTicks = now.Ticks;
            }
        }

        foreach (var entry in ChangeTracker.Entries<Notification>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtTicks = now.Ticks;
            }
        }

        foreach (var entry in ChangeTracker.Entries<TenantMember>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtTicks = now.Ticks;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
