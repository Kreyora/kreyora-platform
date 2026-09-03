using Kreyora.Application.Models;
using Kreyora.Domain.Catalog;

namespace Kreyora.Application.Catalog;

public interface ICatalogService
{
    Task<Result<CatalogProduct>> CreateProductAsync(CreateProductRequest request, CancellationToken cancellationToken = default);
    Task<Result<CatalogProduct>> GetProductAsync(string productId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<CatalogProduct>>> ListProductsAsync(CancellationToken cancellationToken = default);
    Task<Result<CatalogProduct>> UpdateProductAsync(UpdateProductRequest request, CancellationToken cancellationToken = default);
    Task<Result<CatalogProduct>> AddVariantAsync(AddProductVariantRequest request, CancellationToken cancellationToken = default);
    Task<Result<CatalogProduct>> UpdateVariantAsync(UpdateProductVariantRequest request, CancellationToken cancellationToken = default);
    Task<Result<CatalogProduct>> ChangePublicationStateAsync(ChangeProductPublicationStateRequest request, CancellationToken cancellationToken = default);
    Task<Result<CatalogProduct>> ArchiveProductAsync(ArchiveProductRequest request, CancellationToken cancellationToken = default);
}

public sealed record CreateProductRequest(
    string Title,
    string? Description,
    string Slug,
    IReadOnlyList<CreateProductVariantRequest> Variants,
    string IdempotencyKey);

public sealed record CreateProductVariantRequest(
    string Sku,
    string Name,
    IReadOnlyDictionary<string, string>? Options,
    decimal PriceNpr,
    decimal? CompareAtPriceNpr,
    bool IsPublished);

public sealed record UpdateProductRequest(
    string ProductId,
    string Title,
    string? Description,
    string Slug,
    uint ExpectedVersion);

public sealed record AddProductVariantRequest(
    string ProductId,
    string Sku,
    string Name,
    IReadOnlyDictionary<string, string>? Options,
    decimal PriceNpr,
    decimal? CompareAtPriceNpr,
    bool IsPublished,
    uint ExpectedVersion);

public sealed record UpdateProductVariantRequest(
    string ProductId,
    string VariantId,
    string Sku,
    string Name,
    IReadOnlyDictionary<string, string>? Options,
    decimal PriceNpr,
    decimal? CompareAtPriceNpr,
    bool IsPublished,
    uint ExpectedVersion);

public sealed record ChangeProductPublicationStateRequest(string ProductId, ProductPublishState State, uint ExpectedVersion);
public sealed record ArchiveProductRequest(string ProductId, uint ExpectedVersion);

public sealed record CatalogProduct(
    string Id,
    string TenantId,
    string Title,
    string? Description,
    string Slug,
    ProductPublishState PublishState,
    uint Version,
    IReadOnlyList<CatalogVariant> Variants);

public sealed record CatalogVariant(
    string Id,
    string Sku,
    string Name,
    IReadOnlyDictionary<string, string> Options,
    decimal PriceNpr,
    decimal? CompareAtPriceNpr,
    bool IsPublished,
    uint Version);
