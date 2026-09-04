using Kreyora.Domain.Catalog;
using Kreyora.Domain.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class StoreProductPublicationConfiguration : IEntityTypeConfiguration<StoreProductPublication>
{
    public void Configure(EntityTypeBuilder<StoreProductPublication> builder)
    {
        builder.ToTable("store_product_publications");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasMaxLength(26);
        builder.Property(item => item.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.StoreId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.ProductId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.Visibility).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(item => item.CreatedAt).IsRequired();
        builder.Property(item => item.ModifiedAt).IsRequired();
        builder.Property<uint>("xmin").HasColumnName("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
        builder.HasIndex(item => new { item.StoreId, item.ProductId }).IsUnique();
        builder.HasIndex(item => new { item.TenantId, item.ProductId, item.Visibility });
        builder.HasOne<Store>().WithMany().HasForeignKey(item => new { item.TenantId, item.StoreId })
            .HasPrincipalKey(store => new { store.TenantId, store.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(item => new { item.TenantId, item.ProductId })
            .HasPrincipalKey(product => new { product.TenantId, product.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
