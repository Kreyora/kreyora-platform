using Kreyora.Domain.Catalog;
using Kreyora.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("inventory_items", table =>
        {
            table.HasCheckConstraint("ck_inventory_items_on_hand_non_negative", "on_hand_quantity >= 0");
            table.HasCheckConstraint("ck_inventory_items_reserved_non_negative", "reserved_quantity >= 0");
            table.HasCheckConstraint("ck_inventory_items_reserved_not_above_on_hand", "reserved_quantity <= on_hand_quantity");
            table.HasCheckConstraint("ck_inventory_items_low_stock_threshold_non_negative", "low_stock_threshold >= 0");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasMaxLength(26);
        builder.Property(item => item.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.VariantId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.OnHandQuantity).IsRequired();
        builder.Property(item => item.ReservedQuantity).IsRequired();
        builder.Property(item => item.LowStockThreshold).IsRequired();
        builder.Property(item => item.CreatedAt).IsRequired();
        builder.Property(item => item.ModifiedAt).IsRequired();
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .IsConcurrencyToken()
            .ValueGeneratedOnAddOrUpdate();
        builder.HasAlternateKey(item => new { item.TenantId, item.Id });
        builder.HasIndex(item => new { item.TenantId, item.VariantId }).IsUnique();
        builder.HasIndex(item => new { item.TenantId, item.LowStockThreshold, item.ModifiedAt });
        builder.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.VariantId })
            .HasPrincipalKey(variant => new { variant.TenantId, variant.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
