using Kreyora.Domain.Customers;
using Kreyora.Domain.Inventory;
using Kreyora.Domain.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class CheckoutSessionConfiguration : IEntityTypeConfiguration<CheckoutSession>
{
    public void Configure(EntityTypeBuilder<CheckoutSession> builder)
    {
        builder.ToTable("checkout_sessions", table => table.HasCheckConstraint("ck_checkout_sessions_terminal_timestamp", "(state = 'Active' AND completed_at IS NULL AND expired_at IS NULL AND cancelled_at IS NULL) OR (state = 'Completed' AND completed_at IS NOT NULL AND expired_at IS NULL AND cancelled_at IS NULL) OR (state = 'Expired' AND completed_at IS NULL AND expired_at IS NOT NULL AND cancelled_at IS NULL) OR (state = 'Cancelled' AND completed_at IS NULL AND expired_at IS NULL AND cancelled_at IS NOT NULL)"));
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).HasMaxLength(26);
        builder.Property(session => session.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(session => session.StoreId).IsRequired().HasMaxLength(26);
        builder.Property(session => session.CustomerId).HasMaxLength(26);
        builder.Property(session => session.QuoteTokenFingerprint).IsRequired().HasMaxLength(64);
        builder.Property(session => session.State).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(session => session.CustomerName).IsRequired().HasMaxLength(160);
        builder.Property(session => session.CustomerPhone).IsRequired().HasMaxLength(24);
        builder.Property(session => session.CustomerEmail).HasMaxLength(320);
        builder.Property(session => session.AddressLine1).IsRequired().HasMaxLength(160);
        builder.Property(session => session.AddressLine2).HasMaxLength(160);
        builder.Property(session => session.District).IsRequired().HasMaxLength(120);
        builder.Property(session => session.Municipality).HasMaxLength(120);
        builder.Property(session => session.Locality).HasMaxLength(120);
        builder.Property(session => session.Landmark).HasMaxLength(160);
        builder.Property(session => session.PrivacyPolicyFingerprint).IsRequired().HasMaxLength(64);
        builder.Property(session => session.Currency).IsRequired().HasMaxLength(3);
        builder.Property(session => session.DeliveryRuleId).IsRequired().HasMaxLength(26);
        builder.Property(session => session.DeliveryRuleName).IsRequired().HasMaxLength(160);
        builder.Property(session => session.EstimatedEtaText).HasMaxLength(120);
        builder.Property<uint>("xmin").HasColumnName("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
        builder.HasAlternateKey(session => new { session.TenantId, session.Id });
        builder.HasIndex(session => new { session.TenantId, session.ExpiresAt, session.Id }).HasFilter("state = 'Active'");
        builder.HasIndex(session => new { session.TenantId, session.StoreId, session.QuoteTokenFingerprint }).IsUnique().HasFilter("state = 'Active'");
        builder.HasOne<Store>().WithMany().HasForeignKey(session => new { session.TenantId, session.StoreId }).HasPrincipalKey(store => new { store.TenantId, store.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany().HasForeignKey(session => new { session.TenantId, session.CustomerId }).HasPrincipalKey(customer => new { customer.TenantId, customer.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeliveryRule>().WithMany().HasForeignKey(session => new { session.TenantId, session.DeliveryRuleId }).HasPrincipalKey(rule => new { rule.TenantId, rule.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(session => session.Items).WithOne().HasForeignKey(item => new { item.TenantId, item.CheckoutSessionId }).HasPrincipalKey(session => new { session.TenantId, session.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CheckoutSessionItemConfiguration : IEntityTypeConfiguration<CheckoutSessionItem>
{
    public void Configure(EntityTypeBuilder<CheckoutSessionItem> builder)
    {
        builder.ToTable("checkout_session_items");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasMaxLength(26);
        builder.Property(item => item.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.CheckoutSessionId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.InventoryReservationId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.ProductId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.ProductTitle).IsRequired().HasMaxLength(160);
        builder.Property(item => item.VariantId).IsRequired().HasMaxLength(26);
        builder.Property(item => item.VariantName).IsRequired().HasMaxLength(160);
        builder.HasIndex(item => new { item.TenantId, item.CheckoutSessionId, item.VariantId }).IsUnique();
        builder.HasOne<InventoryReservation>().WithMany().HasForeignKey(item => new { item.TenantId, item.InventoryReservationId }).HasPrincipalKey(reservation => new { reservation.TenantId, reservation.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
