using DevFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevFlow.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(320);

        builder.Property(u => u.PasswordHash)
            .IsRequired();

        // Globally unique: login resolves a user by email alone, with no
        // tenant-selection step, so email has to be unambiguous across every
        // tenant a user might belong to (see TenantMember for how one User
        // relates to potentially multiple Tenants).
        builder.HasIndex(u => u.Email).IsUnique();
    }
}
