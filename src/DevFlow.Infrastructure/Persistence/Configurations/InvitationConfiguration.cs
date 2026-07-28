using DevFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevFlow.Infrastructure.Persistence.Configurations;

public class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Email)
            .IsRequired()
            .HasMaxLength(320);

        builder.Property(i => i.TokenHash)
            .IsRequired();

        builder.HasOne(i => i.Tenant)
            .WithMany(t => t.Invitations)
            .HasForeignKey(i => i.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        // No cascade: a User row is never deleted by anything in this system
        // (removing a member deletes their TenantMember, not the User), so
        // this is mostly defensive — Restrict rather than an unexamined
        // default.
        builder.HasOne(i => i.InvitedByUser)
            .WithMany()
            .HasForeignKey(i => i.InvitedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Defensive uniqueness on the hash itself — a collision would mean
        // the token generator is broken, and this makes that loud (a DB
        // constraint violation) instead of silently letting one invitation's
        // token accept a different invitation.
        builder.HasIndex(i => i.TokenHash).IsUnique();

        // Not a uniqueness constraint on (TenantId, Email): re-inviting the
        // same address after a prior invitation expired or was used is
        // valid — multiple historical rows are expected. "Duplicate active
        // invitation" is a business-rule check (InviteMemberInputValidator),
        // not a schema constraint.
        builder.HasIndex(i => new { i.TenantId, i.Email });
    }
}
