using Kreyora.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("product_variants", table =>
        {
            table.HasCheckConstraint("ck_product_variants_price_npr", "price_npr > 0");
            table.HasCheckConstraint("ck_product_variants_compare_at_price_npr", "compare_at_price_npr IS NULL OR compare_at_price_npr >= price_npr");
        });
        builder.HasKey(variant => variant.Id);
        builder.Property(variant => variant.Id).HasMaxLength(26);
        builder.Property(variant => variant.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(variant => variant.ProductId).IsRequired().HasMaxLength(26);
        builder.Property(variant => variant.Sku).IsRequired().HasMaxLength(ProductVariant.SkuMaxLength);
        builder.Property(variant => variant.NormalizedSku).IsRequired().HasMaxLength(ProductVariant.SkuMaxLength);
        builder.Property(variant => variant.Name).IsRequired().HasMaxLength(ProductVariant.NameMaxLength);
        builder.Property(variant => variant.OptionsJson).HasColumnName("options").HasColumnType("jsonb").IsRequired();
        builder.Property(variant => variant.PriceNpr).HasPrecision(18, 2).IsRequired();
        builder.Property(variant => variant.CompareAtPriceNpr).HasPrecision(18, 2);
        builder.Property(variant => variant.IsPublished).IsRequired();
        builder.Property(variant => variant.CreatedAt).IsRequired();
        builder.Property(variant => variant.ModifiedAt).IsRequired();
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .IsConcurrencyToken()
            .ValueGeneratedOnAddOrUpdate();
        builder.HasIndex(variant => new { variant.TenantId, variant.NormalizedSku }).IsUnique();
        builder.HasIndex(variant => new { variant.TenantId, variant.ProductId });
    }
}
