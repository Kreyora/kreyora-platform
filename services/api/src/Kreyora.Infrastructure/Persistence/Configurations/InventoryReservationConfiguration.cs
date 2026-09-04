using Kreyora.Domain.Catalog;
using Kreyora.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class InventoryReservationConfiguration : IEntityTypeConfiguration<InventoryReservation>
{
    public void Configure(EntityTypeBuilder<InventoryReservation> builder)
    {
        builder.ToTable("inventory_reservations", table =>
        {
            table.HasCheckConstraint("ck_inventory_reservations_quantity_positive", "quantity > 0");
            table.HasCheckConstraint("ck_inventory_reservations_terminal_timestamp", "(state = 'Active' AND committed_at IS NULL AND released_at IS NULL AND expired_at IS NULL) OR (state = 'Committed' AND committed_at IS NOT NULL AND released_at IS NULL AND expired_at IS NULL) OR (state = 'Released' AND committed_at IS NULL AND released_at IS NOT NULL AND expired_at IS NULL) OR (state = 'Expired' AND committed_at IS NULL AND released_at IS NULL AND expired_at IS NOT NULL)");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasMaxLength(26);
        builder.Property(item => item.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.InventoryItemId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.VariantId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.Quantity).IsRequired();
        builder.Property(item => item.Source).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(item => item.ReferenceId).IsRequired().HasMaxLength(InventoryReservation.ReferenceIdMaxLength);
        builder.Property(item => item.ActorUserId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.State).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(item => item.ExpiresAt).IsRequired();
        builder.HasAlternateKey(item => new { item.TenantId, item.Id });
        builder.HasIndex(item => new { item.TenantId, item.InventoryItemId, item.State, item.ExpiresAt, item.Id });
        builder.HasIndex(item => new { item.TenantId, item.ExpiresAt, item.Id }).HasFilter("state = 'Active'");
        builder.HasOne<InventoryItem>().WithMany()
            .HasForeignKey(item => new { item.TenantId, item.InventoryItemId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany()
            .HasForeignKey(item => new { item.TenantId, item.VariantId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
