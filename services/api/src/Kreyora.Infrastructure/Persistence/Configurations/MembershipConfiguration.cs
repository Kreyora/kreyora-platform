using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("memberships");
        builder.HasKey(membership => membership.Id);
        builder.Property(membership => membership.Id).HasMaxLength(26);
        builder.Property(membership => membership.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(membership => membership.UserId).IsRequired().HasMaxLength(ApplicationUser.IdLength);
        builder.Property(membership => membership.Role).HasConversion<string>().HasMaxLength(16);
        builder.Property(membership => membership.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(membership => membership.SuspendedAt);
        builder.Property(membership => membership.RevokedAt);
        builder.Property(membership => membership.CreatedAt).IsRequired();
        builder.Property(membership => membership.ModifiedAt).IsRequired();
        builder.HasIndex(membership => new { membership.TenantId, membership.UserId }).IsUnique();
        builder.HasIndex(membership => new { membership.UserId, membership.Status, membership.Role });

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(membership => membership.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(membership => membership.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
