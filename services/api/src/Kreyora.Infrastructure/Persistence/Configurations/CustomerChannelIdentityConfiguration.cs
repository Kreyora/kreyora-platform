using Kreyora.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class CustomerChannelIdentityConfiguration : IEntityTypeConfiguration<CustomerChannelIdentity>
{
    public void Configure(EntityTypeBuilder<CustomerChannelIdentity> builder)
    {
        builder.ToTable("customer_channel_identities");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(26);
        builder.Property(e => e.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(e => e.ConnectionId).IsRequired().HasMaxLength(26);
        builder.Property(e => e.Channel).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.ExternalUserId).IsRequired().HasMaxLength(CustomerChannelIdentity.ExternalUserIdMaxLength);
        builder.Property(e => e.DisplayName).HasMaxLength(CustomerChannelIdentity.DisplayNameMaxLength);
        builder.Property(e => e.Username).HasMaxLength(CustomerChannelIdentity.UsernameMaxLength);
        builder.Property(e => e.CustomerId).HasMaxLength(26);
        builder.Property(e => e.FirstSeenAt).IsRequired();
        builder.Property(e => e.LastSeenAt).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.ModifiedAt).IsRequired();

        builder.Property<uint>("xmin").HasColumnName("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();

        builder.HasAlternateKey(e => new { e.TenantId, e.Id });

        // One identity per provider user per connection (ADR-016); identities never span connections.
        builder.HasIndex(e => new { e.ConnectionId, e.ExternalUserId }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.CustomerId }).HasFilter("customer_id IS NOT NULL");
    }
}
