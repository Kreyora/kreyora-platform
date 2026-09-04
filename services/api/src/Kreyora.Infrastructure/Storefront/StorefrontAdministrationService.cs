using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Models;
using Kreyora.Application.Storefront;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Storefront;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kreyora.Infrastructure.Storefront;

public sealed class StorefrontAdministrationService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    ITenantPermissionAuthorizer permissionAuthorizer,
    IAuditEventService auditEvents,
    IStorefrontCatalogReadService catalog,
    IDeliveryRuleReadService deliveryRules) : IStorefrontAdministrationService
{
    private const string CreateOperation = "store.create";
    private const string ActivateOperation = "store.activate";
    private const string PublicationOperation = "store.publication.set";

    public async Task<Result<StorefrontStore>> GetStoreAsync(CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.StorefrontRead);
        var store = await GetStoreEntityAsync(cancellationToken);
        return store is null
            ? Result<StorefrontStore>.NotFound("A store has not been created for the selected workspace.")
            : Result<StorefrontStore>.Success(Map(store));
    }

    public async Task<Result<StorefrontStore>> CreateStoreAsync(CreateStoreRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.StorefrontWrite);
        var context = tenantContext.RequireCurrent();
        var fingerprint = Fingerprint(request.Settings);

        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var previous = await FindCommandAsync(CreateOperation, request.IdempotencyKey, cancellationToken);
            if (previous is not null)
            {
                if (!string.Equals(previous.RequestFingerprint, fingerprint, StringComparison.Ordinal)) return Result<StorefrontStore>.Conflict("The idempotency key was already used for a different store creation request.");
                var replay = await dbContext.Stores.SingleOrDefaultAsync(store => store.Id == previous.ResourceId, cancellationToken);
                return replay is null ? Result<StorefrontStore>.Conflict("The original store creation operation is incomplete.") : Result<StorefrontStore>.Success(Map(replay));
            }

            if (await dbContext.Stores.AnyAsync(cancellationToken)) return Result<StorefrontStore>.Conflict("A store already exists for this workspace.");
            var store = Store.Create(context.TenantId, ToSettings(request.Settings));
            await EnsurePlatformSlugAvailableAsync(store.NormalizedPlatformSlug, null, cancellationToken);
            dbContext.Stores.Add(store);
            dbContext.StoreCommandIdempotencyRecords.Add(StoreCommandIdempotency.Create(context.TenantId, CreateOperation, request.IdempotencyKey, fingerprint, store.Id));
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditEvents.AppendAsync(new AuditEventWrite("store.created", "store", store.Id), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<StorefrontStore>.Success(Map(store));
        }
        catch (DuplicateStoreValueException exception)
        {
            return Result<StorefrontStore>.Conflict(exception.Message);
        }
        catch (Exception exception) when (IsValidationException(exception))
        {
            return Result<StorefrontStore>.ValidationError(exception.Message);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Result<StorefrontStore>.Conflict("This store slug is already in use or this workspace already has an active store.");
        }
    }

    public async Task<Result<StorefrontStore>> UpdateStoreAsync(UpdateStoreRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.StorefrontWrite);
        var store = await GetStoreEntityAsync(cancellationToken);
        if (store is null) return Result<StorefrontStore>.NotFound("A store has not been created for the selected workspace.");

        try
        {
            SetExpectedVersion(store, request.ExpectedVersion);
            var normalizedSlug = Store.NormalizePlatformSlug(request.Settings.PlatformSlug).ToUpperInvariant();
            await EnsurePlatformSlugAvailableAsync(normalizedSlug, store.Id, cancellationToken);
            store.UpdateSettings(ToSettings(request.Settings));
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditEvents.AppendAsync(new AuditEventWrite("store.settings.updated", "store", store.Id), cancellationToken);
            return Result<StorefrontStore>.Success(Map(store));
        }
        catch (DuplicateStoreValueException exception)
        {
            return Result<StorefrontStore>.Conflict(exception.Message);
        }
        catch (Exception exception) when (IsValidationException(exception))
        {
            return Result<StorefrontStore>.ValidationError(exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<StorefrontStore>.Conflict("The store was changed by another user. Refresh and try again.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Result<StorefrontStore>.Conflict("This store slug is already in use.");
        }
    }

    public async Task<Result<StoreReadiness>> GetReadinessAsync(CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.StorefrontRead);
        var store = await GetStoreEntityAsync(cancellationToken);
        return store is null
            ? Result<StoreReadiness>.NotFound("A store has not been created for the selected workspace.")
            : Result<StoreReadiness>.Success(await BuildReadinessAsync(store, cancellationToken));
    }

    public async Task<Result<StorefrontStore>> ActivateStoreAsync(ActivateStoreRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.StorefrontWrite);
        var context = tenantContext.RequireCurrent();
        var store = await GetStoreEntityAsync(cancellationToken);
        if (store is null) return Result<StorefrontStore>.NotFound("A store has not been created for the selected workspace.");
        var fingerprint = Fingerprint(new { request.ExpectedVersion, store.Id });

        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var previous = await FindCommandAsync(ActivateOperation, request.IdempotencyKey, cancellationToken);
            if (previous is not null)
            {
                if (!string.Equals(previous.RequestFingerprint, fingerprint, StringComparison.Ordinal)) return Result<StorefrontStore>.Conflict("The idempotency key was already used for a different store activation request.");
                var replay = await dbContext.Stores.SingleOrDefaultAsync(item => item.Id == previous.ResourceId, cancellationToken);
                return replay is null ? Result<StorefrontStore>.Conflict("The original store activation operation is incomplete.") : Result<StorefrontStore>.Success(Map(replay));
            }

            SetExpectedVersion(store, request.ExpectedVersion);
            var readiness = await BuildReadinessAsync(store, cancellationToken);
            if (!readiness.CanActivate) return Result<StorefrontStore>.ValidationError($"Store activation is blocked: {string.Join(", ", readiness.Blockers.Select(blocker => blocker.Code))}.");
            store.Activate(DateTimeOffset.UtcNow);
            dbContext.StoreCommandIdempotencyRecords.Add(StoreCommandIdempotency.Create(context.TenantId, ActivateOperation, request.IdempotencyKey, fingerprint, store.Id));
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditEvents.AppendAsync(new AuditEventWrite("store.activated", "store", store.Id), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<StorefrontStore>.Success(Map(store));
        }
        catch (Exception exception) when (IsValidationException(exception))
        {
            return Result<StorefrontStore>.ValidationError(exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<StorefrontStore>.Conflict("The store was changed by another user. Refresh and try again.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Result<StorefrontStore>.Conflict("This workspace already has an active store.");
        }
    }

    public async Task<Result<StorePublicationPage>> ListPublicationsAsync(StorePublicationQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        permissionAuthorizer.Demand(TenantPermissions.StorefrontRead);
        var store = await GetStoreEntityAsync(cancellationToken);
        if (store is null) return Result<StorePublicationPage>.NotFound("A store has not been created for the selected workspace.");
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var publications = dbContext.StoreProductPublications.Where(item => item.StoreId == store.Id).OrderBy(item => item.ProductId);
        var total = await publications.CountAsync(cancellationToken);
        var items = await publications.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return Result<StorePublicationPage>.Success(new StorePublicationPage(items.Select(Map).ToArray(), page, pageSize, total));
    }

    public async Task<Result<StoreProductPublicationItem>> SetProductVisibilityAsync(SetStoreProductVisibilityRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.StorefrontWrite);
        var context = tenantContext.RequireCurrent();
        var store = await GetStoreEntityAsync(cancellationToken);
        if (store is null) return Result<StoreProductPublicationItem>.NotFound("A store has not been created for the selected workspace.");
        var fingerprint = Fingerprint(new { store.Id, request.ProductId, request.Visibility, request.ExpectedVersion });

        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var previous = await FindCommandAsync(PublicationOperation, request.IdempotencyKey, cancellationToken);
            if (previous is not null)
            {
                if (!string.Equals(previous.RequestFingerprint, fingerprint, StringComparison.Ordinal)) return Result<StoreProductPublicationItem>.Conflict("The idempotency key was already used for a different store publication request.");
                var replay = await dbContext.StoreProductPublications.SingleOrDefaultAsync(item => item.Id == previous.ResourceId, cancellationToken);
                return replay is null ? Result<StoreProductPublicationItem>.Conflict("The original store publication operation is incomplete.") : Result<StoreProductPublicationItem>.Success(Map(replay));
            }

            if (request.Visibility == StoreProductVisibility.Visible && !await catalog.IsPublishedPurchasableAsync(request.ProductId, cancellationToken))
            {
                return Result<StoreProductPublicationItem>.ValidationError("Only a published product with a purchasable variant can be visible in the store.");
            }

            var publication = await dbContext.StoreProductPublications.SingleOrDefaultAsync(item => item.StoreId == store.Id && item.ProductId == request.ProductId, cancellationToken);
            if (publication is null)
            {
                publication = StoreProductPublication.Create(context.TenantId, store.Id, request.ProductId, request.Visibility);
                dbContext.StoreProductPublications.Add(publication);
            }
            else
            {
                SetExpectedVersion(publication, request.ExpectedVersion);
                publication.SetVisibility(request.Visibility);
            }

            dbContext.StoreCommandIdempotencyRecords.Add(StoreCommandIdempotency.Create(context.TenantId, PublicationOperation, request.IdempotencyKey, fingerprint, publication.Id));
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditEvents.AppendAsync(new AuditEventWrite(
                request.Visibility == StoreProductVisibility.Visible ? "store.product.visible" : "store.product.hidden",
                "store-product-publication", publication.Id,
                Metadata: $"{{\"productId\":\"{publication.ProductId}\"}}"), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<StoreProductPublicationItem>.Success(Map(publication));
        }
        catch (Exception exception) when (IsValidationException(exception))
        {
            return Result<StoreProductPublicationItem>.ValidationError(exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<StoreProductPublicationItem>.Conflict("The product publication was changed by another user. Refresh and try again.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Result<StoreProductPublicationItem>.Conflict("The product publication was changed concurrently. Refresh and try again.");
        }
    }

    private async Task<Store?> GetStoreEntityAsync(CancellationToken cancellationToken) => await dbContext.Stores.SingleOrDefaultAsync(cancellationToken);

    private async Task<StoreCommandIdempotency?> FindCommandAsync(string operation, string idempotencyKey, CancellationToken cancellationToken) =>
        await dbContext.StoreCommandIdempotencyRecords.SingleOrDefaultAsync(record => record.Operation == operation && record.IdempotencyKey == idempotencyKey, cancellationToken);

    private async Task EnsurePlatformSlugAvailableAsync(string normalizedSlug, string? currentStoreId, CancellationToken cancellationToken)
    {
        if (await dbContext.Stores.IgnoreQueryFilters().AnyAsync(store => store.NormalizedPlatformSlug == normalizedSlug && store.Id != currentStoreId, cancellationToken))
        {
            throw new DuplicateStoreValueException("This store slug is already in use.");
        }
    }

    private async Task<StoreReadiness> BuildReadinessAsync(Store store, CancellationToken cancellationToken)
    {
        var blockers = new List<StoreReadinessBlocker>();
        var profileReady = !string.IsNullOrWhiteSpace(store.DisplayName) && !string.IsNullOrWhiteSpace(store.PlatformSlug) &&
            (!string.IsNullOrWhiteSpace(store.ContactEmail) || !string.IsNullOrWhiteSpace(store.ContactPhone));
        if (!profileReady) blockers.Add(new StoreReadinessBlocker("profile_incomplete", "profile"));
        var policiesReady = !string.IsNullOrWhiteSpace(store.TermsPolicy) && !string.IsNullOrWhiteSpace(store.PrivacyPolicy) &&
            !string.IsNullOrWhiteSpace(store.ReturnsPolicy) && !string.IsNullOrWhiteSpace(store.PaymentPolicy);
        if (!policiesReady) blockers.Add(new StoreReadinessBlocker("policies_incomplete", "policies"));
        var visibleIds = await dbContext.StoreProductPublications.Where(item => item.StoreId == store.Id && item.Visibility == StoreProductVisibility.Visible)
            .Select(item => item.ProductId).ToListAsync(cancellationToken);
        var catalogReady = false;
        foreach (var productId in visibleIds)
        {
            if (await catalog.IsPublishedPurchasableAsync(productId, cancellationToken))
            {
                catalogReady = true;
                break;
            }
        }
        if (!catalogReady) blockers.Add(new StoreReadinessBlocker("catalog_not_ready", "catalog"));
        var deliveryReady = await deliveryRules.HasActiveRulesAsync(store.Id, cancellationToken);
        if (!deliveryReady) blockers.Add(new StoreReadinessBlocker("delivery_not_configured", "delivery"));
        var paymentReady = await deliveryRules.HasActiveCodRuleAsync(store.Id, cancellationToken);
        if (!paymentReady) blockers.Add(new StoreReadinessBlocker("cod_not_configured", "payments"));
        var sections = new[]
        {
            new StoreReadinessSection("profile", profileReady),
            new StoreReadinessSection("policies", policiesReady),
            new StoreReadinessSection("catalog", catalogReady),
            new StoreReadinessSection("delivery", deliveryReady),
            new StoreReadinessSection("payments", paymentReady)
        };
        return new StoreReadiness(blockers.Count == 0, blockers.Count == 0, sections, blockers);
    }

    private static StoreSettings ToSettings(StoreSettingsInput input) => new(
        input.DisplayName, input.PlatformSlug, input.Tagline, input.ThemePreset, input.BrandAccentHex, input.ContactName,
        input.ContactEmail, input.ContactPhone, input.ContactWhatsApp, input.FacebookUrl, input.InstagramUrl, input.TikTokUrl,
        input.TermsPolicy, input.PrivacyPolicy, input.ReturnsPolicy, input.PaymentPolicy);

    private void SetExpectedVersion(Store store, uint expectedVersion) => dbContext.Entry(store).Property<uint>("xmin").OriginalValue = expectedVersion;
    private void SetExpectedVersion(StoreProductPublication publication, uint expectedVersion) => dbContext.Entry(publication).Property<uint>("xmin").OriginalValue = expectedVersion;

    private StorefrontStore Map(Store store) => new(store.Id, store.TenantId, store.DisplayName, store.PlatformSlug, store.Tagline, store.Status,
        store.ThemePreset, store.BrandAccentHex, store.ContactName, store.ContactEmail, store.ContactPhone, store.ContactWhatsApp,
        store.FacebookUrl, store.InstagramUrl, store.TikTokUrl, store.TermsPolicy, store.PrivacyPolicy, store.ReturnsPolicy,
        store.PaymentPolicy, store.ActivatedAt, dbContext.Entry(store).Property<uint>("xmin").CurrentValue);
    private StoreProductPublicationItem Map(StoreProductPublication publication) => new(publication.Id, publication.ProductId, publication.Visibility, dbContext.Entry(publication).Property<uint>("xmin").CurrentValue);

    private static string Fingerprint(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static bool IsValidationException(Exception exception) => exception is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException or DuplicateStoreValueException;
    private static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    private sealed class DuplicateStoreValueException(string message) : InvalidOperationException(message);
}
