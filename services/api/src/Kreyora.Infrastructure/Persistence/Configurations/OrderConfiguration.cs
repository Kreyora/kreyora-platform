using Kreyora.Domain.Customers;
using Kreyora.Domain.Inventory;
using Kreyora.Domain.Orders;
using Kreyora.Domain.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).HasMaxLength(26);
        builder.Property(order => order.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(order => order.StoreId).IsRequired().HasMaxLength(26);
        builder.Property(order => order.CheckoutSessionId).IsRequired().HasMaxLength(26);
        builder.Property(order => order.CustomerId).HasMaxLength(26);
        builder.Property(order => order.OrderNumber).IsRequired().HasMaxLength(32);
        builder.Property(order => order.Source).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(order => order.PaymentMethod).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(order => order.PaymentStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(order => order.FulfilmentStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(order => order.CustomerName).IsRequired().HasMaxLength(160);
        builder.Property(order => order.CustomerPhone).IsRequired().HasMaxLength(24);
        builder.Property(order => order.CustomerEmail).HasMaxLength(320);
        builder.Property(order => order.AddressLine1).IsRequired().HasMaxLength(160);
        builder.Property(order => order.AddressLine2).HasMaxLength(160);
        builder.Property(order => order.District).IsRequired().HasMaxLength(120);
        builder.Property(order => order.Municipality).HasMaxLength(120);
        builder.Property(order => order.Locality).HasMaxLength(120);
        builder.Property(order => order.Landmark).HasMaxLength(160);
        builder.Property(order => order.Currency).IsRequired().HasMaxLength(3);
        builder.Property(order => order.DeliveryRuleId).IsRequired().HasMaxLength(26);
        builder.Property(order => order.DeliveryRuleName).IsRequired().HasMaxLength(160);
        builder.Property(order => order.EstimatedEtaText).HasMaxLength(120);
        builder.Property<uint>("xmin").HasColumnName("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
        builder.HasAlternateKey(order => new { order.TenantId, order.Id });
        builder.HasIndex(order => new { order.TenantId, order.CheckoutSessionId }).IsUnique();
        builder.HasIndex(order => new { order.TenantId, order.OrderNumber }).IsUnique();
        builder.HasIndex(order => new { order.TenantId, order.Status, order.CreatedAt, order.Id });
        builder.HasOne<Store>().WithMany().HasForeignKey(order => new { order.TenantId, order.StoreId }).HasPrincipalKey(store => new { store.TenantId, store.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany().HasForeignKey(order => new { order.TenantId, order.CustomerId }).HasPrincipalKey(customer => new { customer.TenantId, customer.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CheckoutSession>().WithMany().HasForeignKey(order => new { order.TenantId, order.CheckoutSessionId }).HasPrincipalKey(session => new { session.TenantId, session.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeliveryRule>().WithMany().HasForeignKey(order => new { order.TenantId, order.DeliveryRuleId }).HasPrincipalKey(rule => new { rule.TenantId, rule.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(order => order.Items).WithOne().HasForeignKey(item => new { item.TenantId, item.OrderId }).HasPrincipalKey(order => new { order.TenantId, order.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasMaxLength(26);
        builder.Property(item => item.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.OrderId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.InventoryReservationId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.ProductId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.ProductTitle).IsRequired().HasMaxLength(160);
        builder.Property(item => item.VariantId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.VariantName).IsRequired().HasMaxLength(160);
        builder.HasIndex(item => new { item.TenantId, item.OrderId, item.VariantId }).IsUnique();
        builder.HasIndex(item => new { item.TenantId, item.InventoryReservationId }).IsUnique();
        builder.HasOne<InventoryReservation>().WithMany().HasForeignKey(item => new { item.TenantId, item.InventoryReservationId }).HasPrincipalKey(reservation => new { reservation.TenantId, reservation.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
