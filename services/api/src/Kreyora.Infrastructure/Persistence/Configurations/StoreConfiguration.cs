using Kreyora.Domain.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> builder)
    {
        builder.ToTable("stores", table =>
        {
            table.HasCheckConstraint("ck_stores_platform_slug", "platform_slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
            table.HasCheckConstraint("ck_stores_brand_accent", "brand_accent_hex IS NULL OR brand_accent_hex ~ '^#[0-9A-F]{6}$'");
        });
        builder.HasKey(store => store.Id);
        builder.Property(store => store.Id).HasMaxLength(26);
        builder.Property(store => store.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(store => store.DisplayName).IsRequired().HasMaxLength(Store.DisplayNameMaxLength);
        builder.Property(store => store.PlatformSlug).IsRequired().HasMaxLength(Store.PlatformSlugMaxLength);
        builder.Property(store => store.NormalizedPlatformSlug).IsRequired().HasMaxLength(Store.PlatformSlugMaxLength);
        builder.Property(store => store.Tagline).HasMaxLength(Store.TaglineMaxLength);
        builder.Property(store => store.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(store => store.ThemePreset).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(store => store.BrandAccentHex).HasMaxLength(7);
        builder.Property(store => store.ContactName).HasMaxLength(Store.ContactValueMaxLength);
        builder.Property(store => store.ContactEmail).HasMaxLength(Store.ContactValueMaxLength);
        builder.Property(store => store.ContactPhone).HasMaxLength(Store.ContactValueMaxLength);
        builder.Property(store => store.ContactWhatsApp).HasMaxLength(Store.ContactValueMaxLength);
        builder.Property(store => store.FacebookUrl).HasMaxLength(Store.UrlMaxLength);
        builder.Property(store => store.InstagramUrl).HasMaxLength(Store.UrlMaxLength);
        builder.Property(store => store.TikTokUrl).HasMaxLength(Store.UrlMaxLength);
        builder.Property(store => store.TermsPolicy).HasMaxLength(Store.PolicyMaxLength);
        builder.Property(store => store.PrivacyPolicy).HasMaxLength(Store.PolicyMaxLength);
        builder.Property(store => store.ReturnsPolicy).HasMaxLength(Store.PolicyMaxLength);
        builder.Property(store => store.PaymentPolicy).HasMaxLength(Store.PolicyMaxLength);
        builder.Property(store => store.CreatedAt).IsRequired();
        builder.Property(store => store.ModifiedAt).IsRequired();
        builder.Property<uint>("xmin").HasColumnName("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
        builder.HasAlternateKey(store => new { store.TenantId, store.Id });
        builder.HasIndex(store => store.NormalizedPlatformSlug).IsUnique();
        builder.HasIndex(store => new { store.TenantId, store.Status });
        builder.HasIndex(store => store.TenantId).IsUnique().HasFilter("status = 'Active'");
    }
}
