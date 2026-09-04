using Kreyora.Domain.Inventory;
using Kreyora.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class InventoryReservationCommandConfiguration : IEntityTypeConfiguration<InventoryReservationCommand>
{
    public void Configure(EntityTypeBuilder<InventoryReservationCommand> builder)
    {
        builder.ToTable("inventory_reservation_commands");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasMaxLength(26);
        builder.Property(item => item.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.ReservationId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.Operation).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(item => item.IdempotencyKey).IsRequired().HasMaxLength(256);
        builder.Property(item => item.RequestFingerprint).IsRequired().HasMaxLength(64);
        builder.HasIndex(item => new { item.TenantId, item.Operation, item.IdempotencyKey }).IsUnique();
        builder.HasOne<InventoryReservation>().WithMany()
            .HasForeignKey(item => new { item.TenantId, item.ReservationId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
