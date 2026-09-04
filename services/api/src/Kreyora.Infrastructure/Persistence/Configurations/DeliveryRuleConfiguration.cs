using Kreyora.Domain.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class DeliveryRuleConfiguration : IEntityTypeConfiguration<DeliveryRule>
{
    public void Configure(EntityTypeBuilder<DeliveryRule> builder)
    {
        builder.ToTable("delivery_rules", table =>
        {
            table.HasCheckConstraint("ck_delivery_rules_base_fee", "base_fee_npr >= 0");
            table.HasCheckConstraint("ck_delivery_rules_priority", "priority >= 0 AND priority <= 10000");
            table.HasCheckConstraint("ck_delivery_rules_threshold", "(fee_type = 'Threshold' AND free_above_npr > 0) OR (fee_type = 'Flat' AND free_above_npr IS NULL)");
        });
        builder.HasKey(rule => rule.Id);
        builder.Property(rule => rule.Id).HasMaxLength(26);
        builder.Property(rule => rule.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(rule => rule.StoreId).IsRequired().HasMaxLength(26);
        builder.Property(rule => rule.Name).IsRequired().HasMaxLength(DeliveryRule.NameMaxLength);
        builder.Property(rule => rule.Priority).IsRequired();
        builder.Property(rule => rule.FeeType).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(rule => rule.BaseFeeNpr).HasPrecision(18, 2).IsRequired();
        builder.Property(rule => rule.FreeAboveNpr).HasPrecision(18, 2);
        builder.Property(rule => rule.EstimatedEtaText).HasMaxLength(DeliveryRule.EstimatedEtaMaxLength);
        builder.Property(rule => rule.CodAvailable).IsRequired();
        builder.Property(rule => rule.IsActive).IsRequired();
        builder.Property(rule => rule.CreatedAt).IsRequired();
        builder.Property(rule => rule.ModifiedAt).IsRequired();
        builder.Property<uint>("xmin").HasColumnName("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
        builder.HasAlternateKey(rule => new { rule.TenantId, rule.Id });
        builder.HasIndex(rule => new { rule.TenantId, rule.StoreId, rule.IsActive, rule.Priority });
        builder.HasOne<Store>().WithMany().HasForeignKey(rule => new { rule.TenantId, rule.StoreId })
            .HasPrincipalKey(store => new { store.TenantId, store.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(rule => rule.Zones).WithOne().HasForeignKey(zone => new { zone.TenantId, zone.DeliveryRuleId })
            .HasPrincipalKey(rule => new { rule.TenantId, rule.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
