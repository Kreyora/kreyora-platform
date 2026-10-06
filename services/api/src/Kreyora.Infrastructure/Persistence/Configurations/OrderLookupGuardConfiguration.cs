using Kreyora.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

/// <summary>Failed chat order verifications per order (M09-S04 Q4).</summary>
public sealed class OrderLookupGuardConfiguration : IEntityTypeConfiguration<OrderLookupGuard>
{
    public void Configure(EntityTypeBuilder<OrderLookupGuard> builder)
    {
        builder.ToTable("assistant_order_lookup_failures");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).HasMaxLength(26);
        builder.Property(g => g.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(g => g.OrderId).IsRequired().HasMaxLength(26);
        builder.HasIndex(g => new { g.TenantId, g.OrderId }).IsUnique();
        builder.HasOne<Order>().WithMany()
            .HasForeignKey(g => new { g.TenantId, g.OrderId })
            .HasPrincipalKey(o => new { o.TenantId, o.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Property<uint>("xmin").HasColumnName("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
    }
}
