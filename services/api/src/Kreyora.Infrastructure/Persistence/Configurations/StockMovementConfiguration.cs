using Kreyora.Domain.Catalog;
using Kreyora.Domain.Common;
using Kreyora.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements", table =>
        {
            table.HasCheckConstraint("ck_stock_movements_quantity_non_zero", "quantity_delta <> 0");
            table.HasCheckConstraint("ck_stock_movements_actor_provenance", "(actor_kind = 'Member' AND actor_user_id IS NOT NULL) OR (actor_kind = 'CommerceSystem' AND actor_user_id IS NULL)");
        });
        builder.HasKey(movement => movement.Id);
        builder.Property(movement => movement.Id).HasMaxLength(26);
        builder.Property(movement => movement.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(movement => movement.InventoryItemId).IsRequired().HasMaxLength(26);
        builder.Property(movement => movement.VariantId).IsRequired().HasMaxLength(26);
        builder.Property(movement => movement.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(movement => movement.QuantityDelta).IsRequired();
        builder.Property(movement => movement.Reason).IsRequired().HasMaxLength(StockMovement.ReasonMaxLength);
        builder.Property(movement => movement.ActorUserId).HasMaxLength(26);
        builder.Property(movement => movement.ActorKind).HasConversion<string>().HasMaxLength(32).HasDefaultValue(CommerceActorKind.Member).IsRequired();
        builder.Property(movement => movement.IdempotencyKey).IsRequired().HasMaxLength(StockMovement.IdempotencyKeyMaxLength);
        builder.Property(movement => movement.RequestFingerprint).IsRequired().HasMaxLength(64);
        builder.Property(movement => movement.ReferenceType).HasMaxLength(64);
        builder.Property(movement => movement.ReferenceId).HasMaxLength(160);
        builder.Property(movement => movement.CreatedAt).IsRequired();
        builder.Property(movement => movement.ModifiedAt).IsRequired();
        builder.HasIndex(movement => new { movement.TenantId, movement.IdempotencyKey }).IsUnique();
        builder.HasIndex(movement => new { movement.TenantId, movement.InventoryItemId, movement.CreatedAt, movement.Id });
        builder.HasOne<InventoryItem>()
            .WithMany()
            .HasForeignKey(movement => new { movement.TenantId, movement.InventoryItemId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(movement => new { movement.TenantId, movement.VariantId })
            .HasPrincipalKey(variant => new { variant.TenantId, variant.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
