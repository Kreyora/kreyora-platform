using System.Text.Json;
using Kreyora.Domain.Integrations;
using Kreyora.Domain.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class ChannelConnectionConfiguration : IEntityTypeConfiguration<ChannelConnection>
{
    public void Configure(EntityTypeBuilder<ChannelConnection> builder)
    {
        builder.ToTable("channel_connections");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasMaxLength(26);
        builder.Property(c => c.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(c => c.StoreId).HasMaxLength(26);
        builder.Property(c => c.Channel).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(c => c.ExternalAccountId).IsRequired().HasMaxLength(ChannelConnection.ExternalAccountIdMaxLength);
        builder.Property(c => c.DisplayName).IsRequired().HasMaxLength(ChannelConnection.DisplayNameMaxLength);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.OwnsOne(c => c.EncryptedCredentials, b =>
        {
            b.Property(s => s.CiphertextBase64).HasColumnName("credentials_ciphertext").IsRequired();
            b.Property(s => s.IvBase64).HasColumnName("credentials_iv").HasMaxLength(64).IsRequired();
            b.Property(s => s.AuthTagBase64).HasColumnName("credentials_auth_tag").HasMaxLength(64).IsRequired();
            b.Property(s => s.KeyVersion).HasColumnName("credentials_key_version").HasMaxLength(32).IsRequired();
        });

        builder.Property(c => c.Capabilities)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<ChannelCapabilities>(v, (JsonSerializerOptions?)null) ?? ChannelCapabilities.FullSimulator())
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(c => c.HealthSummary).HasMaxLength(ChannelConnection.HealthSummaryMaxLength);
        builder.Property(c => c.HealthDetails).HasMaxLength(ChannelConnection.HealthDetailsMaxLength);
        builder.Property(c => c.WebhookVerificationToken).HasMaxLength(ChannelConnection.WebhookVerificationTokenMaxLength);

        builder.Property<uint>("xmin").HasColumnName("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();

        builder.HasAlternateKey(c => new { c.TenantId, c.Id });

        builder.HasIndex(c => new { c.TenantId, c.Channel, c.ExternalAccountId }).IsUnique();
        builder.HasIndex(c => new { c.TenantId, c.StoreId });
        builder.HasIndex(c => new { c.TenantId, c.Status });

        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(c => new { c.TenantId, c.StoreId })
            .HasPrincipalKey(s => new { s.TenantId, s.Id })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

