using Kreyora.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        builder.ToTable("media_assets", table =>
        {
            table.HasCheckConstraint("ck_media_assets_byte_size", "byte_size > 0");
            table.HasCheckConstraint("ck_media_assets_sort_order", "sort_order IS NULL OR sort_order >= 0");
        });
        builder.HasKey(asset => asset.Id);
        builder.Property(asset => asset.Id).HasMaxLength(26);
        builder.Property(asset => asset.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(asset => asset.ObjectKey).IsRequired().HasMaxLength(MediaAsset.ObjectKeyMaxLength);
        builder.Property(asset => asset.ContentType).IsRequired().HasMaxLength(MediaAsset.ContentTypeMaxLength);
        builder.Property(asset => asset.State).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(asset => asset.ProductId).HasMaxLength(26);
        builder.Property(asset => asset.AltText).HasMaxLength(MediaAsset.AltTextMaxLength);
        builder.HasAlternateKey(asset => new { asset.TenantId, asset.Id });
        builder.HasIndex(asset => new { asset.TenantId, asset.ObjectKey }).IsUnique();
        builder.HasIndex(asset => new { asset.TenantId, asset.State, asset.UploadExpiresAt });
        builder.HasIndex(asset => new { asset.TenantId, asset.ProductId, asset.SortOrder }).IsUnique()
            .HasFilter("product_id IS NOT NULL AND state <> 'Deleted'");
        builder.HasOne<Product>().WithMany().HasForeignKey(asset => new { asset.TenantId, asset.ProductId })
            .HasPrincipalKey(product => new { product.TenantId, product.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
