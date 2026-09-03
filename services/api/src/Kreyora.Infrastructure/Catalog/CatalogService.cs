using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Catalog;
using Kreyora.Application.Models;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Catalog;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kreyora.Infrastructure.Catalog;

public sealed class CatalogService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    ITenantPermissionAuthorizer permissionAuthorizer,
    IAuditEventService auditEvents) : ICatalogService
{
    private const string CreateProductOperation = "catalog.product.create";

    public async Task<Result<CatalogProduct>> CreateProductAsync(CreateProductRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.CatalogWrite);
        var context = tenantContext.RequireCurrent();
        var fingerprint = CreateFingerprint(request);

        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var existing = await dbContext.CatalogCommandIdempotencyRecords
                .SingleOrDefaultAsync(record => record.Operation == CreateProductOperation && record.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (existing is not null)
            {
                if (!string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    return Result<CatalogProduct>.Conflict("The idempotency key was already used for a different create-product request.");
                }

                var replay = await GetProductEntityAsync(existing.ProductId, cancellationToken);
                return replay is null
                    ? Result<CatalogProduct>.Conflict("The original create-product operation is incomplete.")
                    : Result<CatalogProduct>.Success(Map(replay));
            }

            var product = Product.Create(context.TenantId, request.Title, request.Description, request.Slug);
            await EnsureSlugAvailableAsync(product.NormalizedSlug, null, cancellationToken);
            foreach (var variantRequest in request.Variants)
            {
                await EnsureSkuAvailableAsync(variantRequest.Sku, null, cancellationToken);
                product.AddVariant(
                    variantRequest.Sku,
                    variantRequest.Name,
                    variantRequest.Options,
                    variantRequest.PriceNpr,
                    variantRequest.CompareAtPriceNpr,
                    variantRequest.IsPublished);
            }

            dbContext.Products.Add(product);
            dbContext.CatalogCommandIdempotencyRecords.Add(CatalogCommandIdempotency.Create(
                context.TenantId, CreateProductOperation, request.IdempotencyKey, fingerprint, product.Id));
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditEvents.AppendAsync(new AuditEventWrite(
                "catalog.product.created", "product", product.Id,
                Metadata: $"{{\"variantCount\":{product.Variants.Count}}}"), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<CatalogProduct>.Success(Map(product));
        }
        catch (Exception exception) when (IsValidationException(exception))
        {
            return Result<CatalogProduct>.ValidationError(exception.Message);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Result<CatalogProduct>.Conflict("A product with this slug or a variant with this SKU already exists in this workspace.");
        }
    }

    public async Task<Result<CatalogProduct>> GetProductAsync(string productId, CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.CatalogRead);
        var product = await GetProductEntityAsync(productId, cancellationToken);
        return product is null
            ? Result<CatalogProduct>.NotFound("The product does not exist in the selected workspace.")
            : Result<CatalogProduct>.Success(Map(product));
    }

    public async Task<Result<IReadOnlyList<CatalogProduct>>> ListProductsAsync(CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.CatalogRead);
        var products = await dbContext.Products.Include(product => product.Variants)
            .OrderByDescending(product => product.ModifiedAt).ThenBy(product => product.Id)
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<CatalogProduct>>.Success(products.Select(Map).ToArray());
    }

    public async Task<Result<CatalogProduct>> UpdateProductAsync(UpdateProductRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.CatalogWrite);
        var product = await GetProductEntityAsync(request.ProductId, cancellationToken);
        if (product is null)
        {
            return Result<CatalogProduct>.NotFound("The product does not exist in the selected workspace.");
        }

        try
        {
            SetExpectedVersion(product, request.ExpectedVersion);
            var normalizedSlug = Product.NormalizeSlug(request.Slug).ToUpperInvariant();
            await EnsureSlugAvailableAsync(normalizedSlug, product.Id, cancellationToken);
            product.UpdateDetails(request.Title, request.Description, request.Slug);
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditEvents.AppendAsync(new AuditEventWrite("catalog.product.updated", "product", product.Id), cancellationToken);
            return Result<CatalogProduct>.Success(Map(product));
        }
        catch (Exception exception) when (IsValidationException(exception))
        {
            return Result<CatalogProduct>.ValidationError(exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CatalogProduct>.Conflict("The product was changed by another user. Refresh and try again.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Result<CatalogProduct>.Conflict("A product with this slug already exists in this workspace.");
        }
    }

    public async Task<Result<CatalogProduct>> AddVariantAsync(AddProductVariantRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.CatalogWrite);
        var product = await GetProductEntityAsync(request.ProductId, cancellationToken);
        if (product is null)
        {
            return Result<CatalogProduct>.NotFound("The product does not exist in the selected workspace.");
        }

        try
        {
            SetExpectedVersion(product, request.ExpectedVersion);
            await EnsureSkuAvailableAsync(request.Sku, null, cancellationToken);
            var variant = product.AddVariant(request.Sku, request.Name, request.Options, request.PriceNpr, request.CompareAtPriceNpr, request.IsPublished);
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditEvents.AppendAsync(new AuditEventWrite("catalog.variant.created", "product-variant", variant.Id,
                Metadata: $"{{\"productId\":\"{product.Id}\"}}"), cancellationToken);
            return Result<CatalogProduct>.Success(Map(product));
        }
        catch (Exception exception) when (IsValidationException(exception))
        {
            return Result<CatalogProduct>.ValidationError(exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CatalogProduct>.Conflict("The product was changed by another user. Refresh and try again.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Result<CatalogProduct>.Conflict("A variant with this SKU already exists in this workspace.");
        }
    }

    public async Task<Result<CatalogProduct>> UpdateVariantAsync(UpdateProductVariantRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.CatalogWrite);
        var product = await GetProductEntityAsync(request.ProductId, cancellationToken);
        if (product is null)
        {
            return Result<CatalogProduct>.NotFound("The product does not exist in the selected workspace.");
        }

        try
        {
            SetExpectedVersion(product, request.ExpectedVersion);
            await EnsureSkuAvailableAsync(request.Sku, request.VariantId, cancellationToken);
            product.UpdateVariant(request.VariantId, request.Sku, request.Name, request.Options, request.PriceNpr, request.CompareAtPriceNpr, request.IsPublished);
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditEvents.AppendAsync(new AuditEventWrite("catalog.variant.updated", "product-variant", request.VariantId,
                Metadata: $"{{\"productId\":\"{product.Id}\"}}"), cancellationToken);
            return Result<CatalogProduct>.Success(Map(product));
        }
        catch (Exception exception) when (IsValidationException(exception))
        {
            return Result<CatalogProduct>.ValidationError(exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CatalogProduct>.Conflict("The product was changed by another user. Refresh and try again.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Result<CatalogProduct>.Conflict("A variant with this SKU already exists in this workspace.");
        }
    }

    public async Task<Result<CatalogProduct>> ChangePublicationStateAsync(ChangeProductPublicationStateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.CatalogWrite);
        var product = await GetProductEntityAsync(request.ProductId, cancellationToken);
        if (product is null)
        {
            return Result<CatalogProduct>.NotFound("The product does not exist in the selected workspace.");
        }

        try
        {
            SetExpectedVersion(product, request.ExpectedVersion);
            switch (request.State)
            {
                case ProductPublishState.Published:
                    product.Publish();
                    break;
                case ProductPublishState.Unpublished:
                    product.Unpublish();
                    break;
                default:
                    return Result<CatalogProduct>.ValidationError("Only published and unpublished state changes are supported here.");
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await auditEvents.AppendAsync(new AuditEventWrite(
                product.PublishState == ProductPublishState.Published ? "catalog.product.published" : "catalog.product.unpublished",
                "product", product.Id), cancellationToken);
            return Result<CatalogProduct>.Success(Map(product));
        }
        catch (Exception exception) when (IsValidationException(exception))
        {
            return Result<CatalogProduct>.ValidationError(exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CatalogProduct>.Conflict("The product was changed by another user. Refresh and try again.");
        }
    }

    public async Task<Result<CatalogProduct>> ArchiveProductAsync(ArchiveProductRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.CatalogWrite);
        var product = await GetProductEntityAsync(request.ProductId, cancellationToken);
        if (product is null)
        {
            return Result<CatalogProduct>.NotFound("The product does not exist in the selected workspace.");
        }

        try
        {
            SetExpectedVersion(product, request.ExpectedVersion);
            product.Archive();
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditEvents.AppendAsync(new AuditEventWrite("catalog.product.archived", "product", product.Id), cancellationToken);
            return Result<CatalogProduct>.Success(Map(product));
        }
        catch (Exception exception) when (IsValidationException(exception))
        {
            return Result<CatalogProduct>.ValidationError(exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CatalogProduct>.Conflict("The product was changed by another user. Refresh and try again.");
        }
    }

    private async Task<Product?> GetProductEntityAsync(string productId, CancellationToken cancellationToken) =>
        await dbContext.Products.Include(product => product.Variants)
            .SingleOrDefaultAsync(product => product.Id == productId, cancellationToken);

    private async Task EnsureSlugAvailableAsync(string normalizedSlug, string? currentProductId, CancellationToken cancellationToken)
    {
        if (await dbContext.Products.AnyAsync(product => product.NormalizedSlug == normalizedSlug && product.Id != currentProductId, cancellationToken))
        {
            throw new DuplicateCatalogValueException("A product with this slug already exists in this workspace.");
        }
    }

    private async Task EnsureSkuAvailableAsync(string sku, string? currentVariantId, CancellationToken cancellationToken)
    {
        var normalizedSku = string.IsNullOrWhiteSpace(sku) ? string.Empty : sku.Trim().ToUpperInvariant();
        if (await dbContext.ProductVariants.AnyAsync(variant => variant.NormalizedSku == normalizedSku && variant.Id != currentVariantId, cancellationToken))
        {
            throw new DuplicateCatalogValueException("A variant with this SKU already exists in this workspace.");
        }
    }

    private void SetExpectedVersion(Product product, uint expectedVersion) =>
        dbContext.Entry(product).Property<uint>("xmin").OriginalValue = expectedVersion;

    private CatalogProduct Map(Product product) => new(
        product.Id,
        product.TenantId,
        product.Title,
        product.Description,
        product.Slug,
        product.PublishState,
        dbContext.Entry(product).Property<uint>("xmin").CurrentValue,
        product.Variants.OrderBy(variant => variant.CreatedAt).Select(variant => new CatalogVariant(
            variant.Id,
            variant.Sku,
            variant.Name,
            variant.GetOptions(),
            variant.PriceNpr,
            variant.CompareAtPriceNpr,
            variant.IsPublished,
            dbContext.Entry(variant).Property<uint>("xmin").CurrentValue)).ToArray());

    private static string CreateFingerprint(CreateProductRequest request)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            request.Title,
            request.Description,
            request.Slug,
            Variants = request.Variants.Select(variant => new
            {
                variant.Sku,
                variant.Name,
                Options = variant.Options?.OrderBy(option => option.Key, StringComparer.OrdinalIgnoreCase),
                variant.PriceNpr,
                variant.CompareAtPriceNpr,
                variant.IsPublished
            })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool IsValidationException(Exception exception) =>
        exception is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException or DuplicateCatalogValueException;

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private sealed class DuplicateCatalogValueException(string message) : InvalidOperationException(message);
}
