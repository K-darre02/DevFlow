using DevFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevFlow.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Title).IsRequired();
        builder.Property(n => n.Message).IsRequired();

        builder.HasOne(n => n.Tenant)
            .WithMany()
            .HasForeignKey(n => n.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        // Cascade, unlike ActivityLog's SetNull on User: a notification only
        // has meaning in the context of the specific person it's for — if
        // that User row were ever gone, there's no one left to read it.
        builder.HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Dominant access pattern (NotificationService): scoped to
        // (TenantId, UserId) — the query filter itself, not just an
        // optional caller filter like ActivityLog's — then newest first
        // (CreatedAtTicks) or narrowed to unread only.
        builder.HasIndex(n => new { n.TenantId, n.UserId, n.CreatedAtTicks });
        builder.HasIndex(n => new { n.TenantId, n.UserId, n.IsRead });
    }
}
