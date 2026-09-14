using Kreyora.Domain.Catalog;
using Kreyora.Domain.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class StorePaymentConfigurationConfiguration : IEntityTypeConfiguration<StorePaymentConfiguration>
{
    public void Configure(EntityTypeBuilder<StorePaymentConfiguration> builder)
    {
        builder.ToTable("store_payment_configurations", table =>
        {
            table.HasCheckConstraint("ck_store_payment_configurations_at_least_one", "cod_enabled = TRUE OR merchant_qr_enabled = TRUE");
        });

        builder.HasKey(config => config.Id);
        builder.Property(config => config.Id).HasMaxLength(26);
        builder.Property(config => config.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(config => config.StoreId).IsRequired().HasMaxLength(26);
        builder.Property(config => config.CodEnabled).IsRequired();
        builder.Property(config => config.MerchantQrEnabled).IsRequired();
        builder.Property(config => config.MerchantQrInstructions).HasMaxLength(StorePaymentConfiguration.InstructionsMaxLength);
        builder.Property(config => config.MerchantQrMediaAssetId).HasMaxLength(26);

        builder.HasAlternateKey(config => new { config.TenantId, config.Id });
        builder.HasIndex(config => new { config.TenantId, config.StoreId }).IsUnique();

        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(config => new { config.TenantId, config.StoreId })
            .HasPrincipalKey(store => new { store.TenantId, store.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<MediaAsset>()
            .WithMany()
            .HasForeignKey(config => new { config.TenantId, config.MerchantQrMediaAssetId })
            .HasPrincipalKey(asset => new { asset.TenantId, asset.Id })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

