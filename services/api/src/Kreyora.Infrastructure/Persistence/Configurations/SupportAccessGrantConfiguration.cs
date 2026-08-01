using Kreyora.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class SupportAccessGrantConfiguration : IEntityTypeConfiguration<SupportAccessGrant>
{
    public void Configure(EntityTypeBuilder<SupportAccessGrant> builder)
    {
        builder.ToTable("support_access_grants");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasMaxLength(26);
        builder.Property(item => item.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.SupportUserId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.GrantedByUserId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.Reason).IsRequired().HasMaxLength(SupportAccessGrant.ReasonMaxLength);
        builder.Property(item => item.ExpiresAt).IsRequired();
        builder.Property(item => item.RevokedAt);
        builder.Property(item => item.RevokedByUserId).HasMaxLength(26);
        builder.Property(item => item.CreatedAt).IsRequired();
        builder.Property(item => item.ModifiedAt).IsRequired();
        builder.HasIndex(item => new { item.TenantId, item.SupportUserId, item.ExpiresAt });
    }
}
