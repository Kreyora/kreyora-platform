using Kreyora.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kreyora.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products", table =>
        {
            table.HasCheckConstraint("ck_products_slug", "slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
        });
        builder.HasKey(product => product.Id);
        builder.Property(product => product.Id).HasMaxLength(26);
        builder.Property(product => product.TenantId).IsRequired().HasMaxLength(26);
        builder.Property(product => product.Title).IsRequired().HasMaxLength(Product.TitleMaxLength);
        builder.Property(product => product.Description).HasMaxLength(Product.DescriptionMaxLength);
        builder.Property(product => product.Slug).IsRequired().HasMaxLength(Product.SlugMaxLength);
        builder.Property(product => product.NormalizedSlug).IsRequired().HasMaxLength(Product.SlugMaxLength);
        builder.Property(product => product.PublishState).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(product => product.CreatedAt).IsRequired();
        builder.Property(product => product.ModifiedAt).IsRequired();
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .IsConcurrencyToken()
            .ValueGeneratedOnAddOrUpdate();
        builder.HasIndex(product => new { product.TenantId, product.NormalizedSlug }).IsUnique();
        builder.HasIndex(product => new { product.TenantId, product.PublishState, product.ModifiedAt });
        builder.HasAlternateKey(product => new { product.TenantId, product.Id });
        builder.HasMany(product => product.Variants)
            .WithOne()
            .HasForeignKey(variant => new { variant.TenantId, variant.ProductId })
            .HasPrincipalKey(product => new { product.TenantId, product.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(product => product.Variants).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
