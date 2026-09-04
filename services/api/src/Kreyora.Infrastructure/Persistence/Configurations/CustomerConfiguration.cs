using Kreyora.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(customer => customer.Id);
        builder.Property(customer => customer.Id).HasMaxLength(26);
        builder.Property(customer => customer.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(customer => customer.DisplayName).IsRequired().HasMaxLength(160);
        builder.Property(customer => customer.Phone).IsRequired().HasMaxLength(24);
        builder.Property(customer => customer.NormalizedPhone).IsRequired().HasMaxLength(24);
        builder.Property(customer => customer.Email).HasMaxLength(320);
        builder.Property(customer => customer.NormalizedEmail).HasMaxLength(320);
        builder.Property(customer => customer.PrivacyPolicyFingerprint).IsRequired().HasMaxLength(64);
        builder.HasAlternateKey(customer => new { customer.TenantId, customer.Id });
        builder.HasIndex(customer => new { customer.TenantId, customer.NormalizedPhone }).IsUnique();
        builder.HasIndex(customer => new { customer.TenantId, customer.NormalizedEmail }).IsUnique().HasFilter("normalized_email IS NOT NULL");
    }
}
