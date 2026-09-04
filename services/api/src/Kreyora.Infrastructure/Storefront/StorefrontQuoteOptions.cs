using System.ComponentModel.DataAnnotations;

namespace Kreyora.Infrastructure.Storefront;

public sealed class StorefrontQuoteOptions
{
    public const string SectionName = "Storefront:Quote";

    [Range(5, 30)]
    public int LifetimeMinutes { get; set; } = 10;
}
