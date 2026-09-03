using System.Text.Json;
using Kreyora.Domain.Common;

namespace Kreyora.Domain.Catalog;

public sealed class ProductVariant : BaseEntity, ITenantOwned
{
    public const int SkuMaxLength = 100;
    public const int NameMaxLength = 160;

    private ProductVariant()
    {
    }

    public string TenantId { get; private set; } = string.Empty;
    public string ProductId { get; private set; } = string.Empty;
    public string Sku { get; private set; } = string.Empty;
    public string NormalizedSku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string OptionsJson { get; private set; } = "{}";
    public decimal PriceNpr { get; private set; }
    public decimal? CompareAtPriceNpr { get; private set; }
    public bool IsPublished { get; private set; }

    public static ProductVariant Create(
        string tenantId,
        string productId,
        string sku,
        string name,
        IReadOnlyDictionary<string, string>? options,
        decimal priceNpr,
        decimal? compareAtPriceNpr,
        bool isPublished)
    {
        var variant = new ProductVariant
        {
            TenantId = RequireId(tenantId, nameof(tenantId)),
            ProductId = RequireId(productId, nameof(productId))
        };
        variant.Update(sku, name, options, priceNpr, compareAtPriceNpr, isPublished);
        return variant;
    }

    public void Update(
        string sku,
        string name,
        IReadOnlyDictionary<string, string>? options,
        decimal priceNpr,
        decimal? compareAtPriceNpr,
        bool isPublished)
    {
        Sku = Require(sku, nameof(sku), SkuMaxLength);
        NormalizedSku = Sku.ToUpperInvariant();
        Name = Require(name, nameof(name), NameMaxLength);
        OptionsJson = SerializeOptions(options);
        ValidatePrice(priceNpr, compareAtPriceNpr);
        PriceNpr = priceNpr;
        CompareAtPriceNpr = compareAtPriceNpr;
        IsPublished = isPublished;
    }

    public IReadOnlyDictionary<string, string> GetOptions() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(OptionsJson)
        ?? new Dictionary<string, string>();

    public bool IsPublishable => IsPublished && PriceNpr > 0 && !string.IsNullOrWhiteSpace(Sku);

    private static void ValidatePrice(decimal priceNpr, decimal? compareAtPriceNpr)
    {
        if (priceNpr <= 0 || HasMoreThanTwoDecimalPlaces(priceNpr))
        {
            throw new ArgumentOutOfRangeException(nameof(priceNpr), "NPR price must be positive with at most two decimal places.");
        }

        if (compareAtPriceNpr is not null &&
            (compareAtPriceNpr < priceNpr || HasMoreThanTwoDecimalPlaces(compareAtPriceNpr.Value)))
        {
            throw new ArgumentOutOfRangeException(nameof(compareAtPriceNpr), "Compare-at NPR price cannot be lower than the sale price and must have at most two decimal places.");
        }
    }

    private static bool HasMoreThanTwoDecimalPlaces(decimal value) => decimal.Truncate(value * 100) != value * 100;

    private static string SerializeOptions(IReadOnlyDictionary<string, string>? options)
    {
        if (options is null || options.Count == 0)
        {
            return "{}";
        }

        var normalized = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in options)
        {
            var normalizedKey = Require(key, nameof(options), NameMaxLength);
            if (!normalized.TryAdd(normalizedKey, Require(value, nameof(options), NameMaxLength)))
            {
                throw new ArgumentException("Variant option names must be unique.", nameof(options));
            }
        }

        return JsonSerializer.Serialize(normalized);
    }

    private static string RequireId(string value, string parameterName) => Require(value, parameterName, 26);

    private static string Require(string value, string parameterName, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A value is required.", parameterName)
            : value.Trim();
        return normalized.Length > maxLength
            ? throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maxLength} characters.")
            : normalized;
    }
}
