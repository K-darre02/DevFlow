using DevFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevFlow.Infrastructure.Persistence.Configurations;

public class TaskAttachmentConfiguration : IEntityTypeConfiguration<TaskAttachment>
{
    public void Configure(EntityTypeBuilder<TaskAttachment> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.FileName).IsRequired().HasMaxLength(255);
        builder.Property(a => a.BlobKey).IsRequired();
        builder.Property(a => a.ContentType).IsRequired().HasMaxLength(255);

        builder.HasOne(a => a.Tenant)
            .WithMany()
            .HasForeignKey(a => a.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        // Cascade: an attachment only has meaning attached to its task, so
        // its *row* should go when the task does — same reasoning as
        // Project -> TaskItems. This only cascades the database row, not
        // the underlying blob in storage; TaskService doesn't know about
        // attachments (a deliberate boundary — see AttachmentService), so a
        // task deleted with attachments still attached leaves orphaned blob
        // files behind. Accepted for this system's scale, the same way
        // other out-of-scope cleanup jobs are (docs/devflow/07-quality-attributes.md);
        // the fix, if it's ever needed, is a periodic reconciliation job
        // comparing blob storage against TaskAttachment rows, not coupling
        // TaskService to IBlobStorageService.
        builder.HasOne(a => a.Task)
            .WithMany()
            .HasForeignKey(a => a.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, matching Invitation.InvitedByUser: nothing in this
        // system deletes a User today, so this is defensive rather than
        // load-bearing — but "who uploaded this" shouldn't silently vanish
        // if that ever changes.
        builder.HasOne(a => a.UploadedByUser)
            .WithMany()
            .HasForeignKey(a => a.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Defensive uniqueness on the storage key itself, same rationale as
        // Invitation.TokenHash — a collision would mean the key generator is
        // broken, and this makes that loud instead of silently overwriting
        // a different attachment's blob.
        builder.HasIndex(a => a.BlobKey).IsUnique();

        // Dominant access pattern: list attachments for one task, tenant-scoped.
        builder.HasIndex(a => new { a.TenantId, a.TaskId });
    }
}
