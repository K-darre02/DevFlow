using DevFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevFlow.Infrastructure.Persistence.Configurations;

public class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Description)
            .IsRequired();

        builder.HasOne(a => a.Tenant)
            .WithMany()
            .HasForeignKey(a => a.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        // SetNull rather than Restrict/Cascade: nothing in this system
        // deletes a User today (see InvitationConfiguration), but if that
        // ever changes, history should survive with an anonymized actor
        // rather than either blocking the delete or vanishing.
        builder.HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // EntityId deliberately has no foreign key: which table it points
        // into depends on EntityType (Project/Task/Invitation/TeamMember),
        // and EF Core/relational FKs can't target a different table per row.

        // The dominant access pattern (GetActivitiesAsync): tenant-scoped,
        // newest first — CreatedAtTicks, not CreatedAt, is what queries
        // actually sort by (see ActivityLog.CreatedAtTicks). EntityType/
        // UserId/ActivityType filters are applied on top of that same scan
        // rather than getting their own indexes — not worth it at this
        // system's expected activity-log volume.
        builder.HasIndex(a => new { a.TenantId, a.CreatedAtTicks });
    }
}
