using Kreyora.Domain.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class DeliveryRuleZoneConfiguration : IEntityTypeConfiguration<DeliveryRuleZone>
{
    public void Configure(EntityTypeBuilder<DeliveryRuleZone> builder)
    {
        builder.ToTable("delivery_rule_zones");
        builder.HasKey(zone => zone.Id);
        builder.Property(zone => zone.Id).HasMaxLength(26);
        builder.Property(zone => zone.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(zone => zone.DeliveryRuleId).IsRequired().HasMaxLength(26);
        builder.Property(zone => zone.District).IsRequired().HasMaxLength(DeliveryRuleZone.LocationMaxLength);
        builder.Property(zone => zone.NormalizedDistrict).IsRequired().HasMaxLength(DeliveryRuleZone.LocationMaxLength);
        builder.Property(zone => zone.Municipality).HasMaxLength(DeliveryRuleZone.LocationMaxLength);
        builder.Property(zone => zone.NormalizedMunicipality).HasMaxLength(DeliveryRuleZone.LocationMaxLength);
        builder.Property(zone => zone.Locality).HasMaxLength(DeliveryRuleZone.LocationMaxLength);
        builder.Property(zone => zone.NormalizedLocality).HasMaxLength(DeliveryRuleZone.LocationMaxLength);
        builder.Property(zone => zone.CreatedAt).IsRequired();
        builder.Property(zone => zone.ModifiedAt).IsRequired();
        builder.HasIndex(zone => new { zone.DeliveryRuleId, zone.NormalizedDistrict, zone.NormalizedMunicipality, zone.NormalizedLocality }).IsUnique();
        builder.HasIndex(zone => new { zone.TenantId, zone.NormalizedDistrict, zone.NormalizedMunicipality, zone.NormalizedLocality });
    }
}
