using System.Text.RegularExpressions;
using Kreyora.Domain.Common;

namespace Kreyora.Domain.Catalog;

public sealed class Product : BaseEntity, ITenantOwned
{
    public const int TitleMaxLength = 160;
    public const int DescriptionMaxLength = 8_000;
    public const int SlugMaxLength = 160;

    private static readonly Regex SlugPattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);
    private readonly List<ProductVariant> variants = [];

    private Product()
    {
    }

    public string TenantId { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Slug { get; private set; } = string.Empty;
    public string NormalizedSlug { get; private set; } = string.Empty;
    public ProductPublishState PublishState { get; private set; }
    public IReadOnlyCollection<ProductVariant> Variants => variants.AsReadOnly();

    public static Product Create(string tenantId, string title, string? description, string slug)
    {
        var product = new Product
        {
            TenantId = Require(tenantId, nameof(tenantId), 26),
            PublishState = ProductPublishState.Draft
        };
        product.UpdateDetails(title, description, slug);
        return product;
    }

    public void UpdateDetails(string title, string? description, string slug)
    {
        EnsureNotArchived();
        Title = Require(title, nameof(title), TitleMaxLength);
        Description = Optional(description, DescriptionMaxLength);
        Slug = NormalizeSlug(slug);
        NormalizedSlug = Slug.ToUpperInvariant();
    }

    public ProductVariant AddVariant(
        string sku,
        string name,
        IReadOnlyDictionary<string, string>? options,
        decimal priceNpr,
        decimal? compareAtPriceNpr,
        bool isPublished)
    {
        EnsureNotArchived();
        var variant = ProductVariant.Create(TenantId, Id, sku, name, options, priceNpr, compareAtPriceNpr, isPublished);
        if (variants.Any(item => item.NormalizedSku == variant.NormalizedSku))
        {
            throw new InvalidOperationException("Product variant SKU must be unique within the product.");
        }

        variants.Add(variant);
        Touch();
        return variant;
    }

    public void UpdateVariant(
        string variantId,
        string sku,
        string name,
        IReadOnlyDictionary<string, string>? options,
        decimal priceNpr,
        decimal? compareAtPriceNpr,
        bool isPublished)
    {
        EnsureNotArchived();
        var variant = variants.SingleOrDefault(item => item.Id == variantId)
            ?? throw new InvalidOperationException("The product variant does not exist.");
        var normalizedSku = Require(sku, nameof(sku), ProductVariant.SkuMaxLength).ToUpperInvariant();
        if (variants.Any(item => item.Id != variantId && item.NormalizedSku == normalizedSku))
        {
            throw new InvalidOperationException("Product variant SKU must be unique within the product.");
        }

        variant.Update(sku, name, options, priceNpr, compareAtPriceNpr, isPublished);
        Touch();
    }

    public void Publish()
    {
        EnsureNotArchived();
        if (!variants.Any(variant => variant.IsPublishable))
        {
            throw new InvalidOperationException("A product requires at least one published valid variant before publication.");
        }

        PublishState = ProductPublishState.Published;
    }

    public void Unpublish()
    {
        EnsureNotArchived();
        if (PublishState != ProductPublishState.Published)
        {
            throw new InvalidOperationException("Only a published product can be unpublished.");
        }

        PublishState = ProductPublishState.Unpublished;
    }

    public void Archive()
    {
        EnsureNotArchived();
        PublishState = ProductPublishState.Archived;
    }

    private void Touch() => ModifiedAt = DateTimeOffset.UtcNow;

    public static string NormalizeSlug(string slug)
    {
        var normalized = Require(slug, nameof(slug), SlugMaxLength).ToLowerInvariant();
        if (!SlugPattern.IsMatch(normalized))
        {
            throw new ArgumentException("Product slug must contain lowercase letters, numbers, and single hyphens.", nameof(slug));
        }

        return normalized;
    }

    private void EnsureNotArchived()
    {
        if (PublishState == ProductPublishState.Archived)
        {
            throw new InvalidOperationException("Archived products cannot be changed.");
        }
    }

    private static string Require(string value, string parameterName, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A value is required.", parameterName)
            : value.Trim();
        return normalized.Length > maxLength
            ? throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maxLength} characters.")
            : normalized;
    }

    private static string? Optional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length > maxLength
            ? throw new ArgumentOutOfRangeException(nameof(value), $"Value cannot exceed {maxLength} characters.")
            : normalized;
    }
}
