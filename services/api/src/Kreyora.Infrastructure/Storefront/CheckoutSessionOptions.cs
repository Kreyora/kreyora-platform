using System.ComponentModel.DataAnnotations;

namespace Kreyora.Infrastructure.Storefront;

public sealed class CheckoutSessionOptions
{
    public const string SectionName = "Storefront:CheckoutSession";
    [Range(1, 30)] public int LifetimeMinutes { get; init; } = 10;
    [Range(1, 90)] public int PiiReviewDays { get; init; } = 30;
    [Range(1, 1_000)] public int ExpiryBatchSize { get; init; } = 100;
}
