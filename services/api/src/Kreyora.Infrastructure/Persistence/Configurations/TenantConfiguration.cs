using Kreyora.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(tenant => tenant.Id);
        builder.Property(tenant => tenant.Id).HasMaxLength(26);
        builder.Property(tenant => tenant.DisplayName).IsRequired().HasMaxLength(Tenant.DisplayNameMaxLength);
        builder.Property(tenant => tenant.Slug).IsRequired().HasMaxLength(Tenant.SlugMaxLength);
        builder.Property(tenant => tenant.NormalizedSlug).IsRequired().HasMaxLength(Tenant.SlugMaxLength);
        builder.Property(tenant => tenant.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(tenant => tenant.OnboardingState).HasConversion<string>().HasMaxLength(16);
        builder.Property(tenant => tenant.CreatedAt).IsRequired();
        builder.Property(tenant => tenant.ModifiedAt).IsRequired();
        builder.HasIndex(tenant => tenant.NormalizedSlug).IsUnique();
    }
}
