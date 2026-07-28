using DevFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevFlow.Infrastructure.Persistence.Configurations;

public class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title)
            .IsRequired()
            .HasMaxLength(500);

        // Matches CreateTaskRequest/UpdateTaskRequest's [MaxLength(4000)] —
        // defense-in-depth the same way Title's bound is, in case a caller
        // ever reaches this column some way other than those two DTOs.
        builder.Property(t => t.Description)
            .HasMaxLength(4000);

        builder.HasOne(t => t.Project)
            .WithMany(p => p.TaskItems)
            .HasForeignKey(t => t.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict (not Cascade) here: TaskItems are already cleaned up
        // transitively when their Project is deleted (Tenant -> Project ->
        // TaskItem, both Cascade above). A second Cascade path direct from
        // Tenant would be redundant and, on some providers, invalid.
        builder.HasOne(t => t.Tenant)
            .WithMany()
            .HasForeignKey(t => t.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.AssigneeUser)
            .WithMany()
            .HasForeignKey(t => t.AssigneeUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Application-managed optimistic concurrency token — see
        // DevFlow.Domain/Entities/TaskItem.cs for why this isn't a byte[]
        // rowversion. IsConcurrencyToken() alone (no ValueGeneratedOnAddOrUpdate)
        // because EF Core doesn't generate this value — DevFlowDbContext does,
        // in SaveChangesAsync.
        builder.Property(t => t.Version).IsConcurrencyToken();

        // Serves the board query directly: tasks for a project, filtered/grouped by status.
        builder.HasIndex(t => new { t.TenantId, t.ProjectId, t.Status });
    }
}
