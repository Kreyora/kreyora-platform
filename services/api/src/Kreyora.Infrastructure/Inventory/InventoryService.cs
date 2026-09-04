using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Inventory;
using Kreyora.Application.Models;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Catalog;
using Kreyora.Domain.Inventory;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kreyora.Infrastructure.Inventory;

public sealed class InventoryService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    ITenantPermissionAuthorizer permissionAuthorizer,
    IAuditEventService auditEvents) : IInventoryService
{
    private const int MaxSerializableAttempts = 3;

    public async Task<Result<StockAdjustmentResult>> AdjustStockAsync(StockAdjustmentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.InventoryWrite);
        var context = tenantContext.RequireCurrent();

        try
        {
            request = Normalize(request);
            for (var attempt = 1; attempt <= MaxSerializableAttempts; attempt++)
            {
                try
                {
                    return await AdjustStockOnceAsync(context, request, cancellationToken);
                }
                catch (DbUpdateException exception) when (attempt < MaxSerializableAttempts && IsRetryable(exception))
                {
                    dbContext.ChangeTracker.Clear();
                }
            }

            return Result<StockAdjustmentResult>.Conflict("The stock adjustment conflicted with another update. Please retry.");
        }
        catch (Exception exception) when (IsValidationException(exception))
        {
            dbContext.ChangeTracker.Clear();
            return Result<StockAdjustmentResult>.ValidationError(exception.Message);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Result<StockAdjustmentResult>.Conflict("The stock adjustment conflicted with another update. Please retry.");
        }
    }

    public async Task<Result<InventoryBalance>> GetInventoryAsync(string variantId, CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.InventoryRead);
        var item = await dbContext.InventoryItems.SingleOrDefaultAsync(candidate => candidate.VariantId == variantId, cancellationToken);
        return item is null
            ? Result<InventoryBalance>.NotFound("No tracked inventory exists for this variant in the selected workspace.")
            : Result<InventoryBalance>.Success(Map(item));
    }

    public async Task<Result<InventoryMovementPage>> GetStockMovementsAsync(
        string variantId,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.InventoryRead);
        var item = await dbContext.InventoryItems.SingleOrDefaultAsync(candidate => candidate.VariantId == variantId, cancellationToken);
        if (item is null)
        {
            return Result<InventoryMovementPage>.NotFound("No tracked inventory exists for this variant in the selected workspace.");
        }

        try
        {
            pageSize = Math.Clamp(pageSize, 1, 100);
            var marker = DecodeCursor(cursor);
            var query = dbContext.StockMovements.AsNoTracking()
                .Where(movement => movement.InventoryItemId == item.Id);
            if (marker is not null)
            {
                query = query.Where(movement => movement.CreatedAt < marker.Value.CreatedAt ||
                    (movement.CreatedAt == marker.Value.CreatedAt && movement.Id.CompareTo(marker.Value.Id) < 0));
            }

            var movements = await query.OrderByDescending(movement => movement.CreatedAt).ThenByDescending(movement => movement.Id)
                .Take(pageSize + 1)
                .ToListAsync(cancellationToken);
            var hasMore = movements.Count > pageSize;
            var items = movements.Take(pageSize).Select(Map).ToArray();
            var last = items.LastOrDefault();
            return Result<InventoryMovementPage>.Success(new InventoryMovementPage(
                items,
                hasMore && last is not null ? EncodeCursor(last.CreatedAt, last.Id) : null));
        }
        catch (ArgumentException exception)
        {
            return Result<InventoryMovementPage>.ValidationError(exception.Message);
        }
    }

    public async Task<Result<IReadOnlyList<InventoryBalance>>> GetLowStockAsync(CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.InventoryRead);
        var items = await dbContext.InventoryItems
            .Where(item => item.LowStockThreshold > 0 && item.OnHandQuantity - item.ReservedQuantity <= item.LowStockThreshold)
            .OrderBy(item => item.OnHandQuantity - item.ReservedQuantity)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<InventoryBalance>>.Success(items.Select(Map).ToArray());
    }

    public async Task<Result<InventoryBalance>> SetLowStockThresholdAsync(
        SetLowStockThresholdRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.InventoryWrite);
        var item = await dbContext.InventoryItems.SingleOrDefaultAsync(candidate => candidate.VariantId == request.VariantId, cancellationToken);
        if (item is null)
        {
            return Result<InventoryBalance>.NotFound("No tracked inventory exists for this variant in the selected workspace.");
        }

        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            SetExpectedVersion(item, request.ExpectedVersion);
            item.SetLowStockThreshold(request.Threshold);
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditEvents.AppendAsync(new AuditEventWrite(
                "inventory.low-stock-threshold.updated", "inventory-item", item.Id,
                Metadata: $"{{\"variantId\":\"{item.VariantId}\",\"threshold\":{item.LowStockThreshold}}}"), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<InventoryBalance>.Success(Map(item));
        }
        catch (Exception exception) when (IsValidationException(exception))
        {
            return Result<InventoryBalance>.ValidationError(exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<InventoryBalance>.Conflict("The inventory threshold was changed by another user. Refresh and try again.");
        }
    }

    public async Task<Result<InventoryReconciliation>> ReconcileInventoryAsync(string variantId, CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.InventoryRead);
        var item = await dbContext.InventoryItems.SingleOrDefaultAsync(candidate => candidate.VariantId == variantId, cancellationToken);
        if (item is null)
        {
            return Result<InventoryReconciliation>.NotFound("No tracked inventory exists for this variant in the selected workspace.");
        }

        var ledgerTotal = await dbContext.StockMovements.Where(movement => movement.InventoryItemId == item.Id)
            .Select(movement => (int?)movement.QuantityDelta)
            .SumAsync(cancellationToken) ?? 0;
        return Result<InventoryReconciliation>.Success(new InventoryReconciliation(
            item.Id,
            item.VariantId,
            ledgerTotal,
            item.OnHandQuantity,
            ledgerTotal == item.OnHandQuantity));
    }

    private async Task<Result<StockAdjustmentResult>> AdjustStockOnceAsync(
        TenantContext context,
        StockAdjustmentRequest request,
        CancellationToken cancellationToken)
    {
        var quantityDelta = GetQuantityDelta(request.Type, request.Quantity);
        var fingerprint = CreateFingerprint(request);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var existingMovement = await dbContext.StockMovements.SingleOrDefaultAsync(
            movement => movement.IdempotencyKey == request.IdempotencyKey,
            cancellationToken);
        if (existingMovement is not null)
        {
            if (!string.Equals(existingMovement.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return Result<StockAdjustmentResult>.Conflict("The idempotency key was already used for a different stock adjustment.");
            }

            var replayItem = await dbContext.InventoryItems.SingleAsync(item => item.Id == existingMovement.InventoryItemId, cancellationToken);
            return Result<StockAdjustmentResult>.Success(new StockAdjustmentResult(Map(replayItem), Map(existingMovement), true));
        }

        var variant = await dbContext.ProductVariants.SingleOrDefaultAsync(candidate => candidate.Id == request.VariantId, cancellationToken);
        if (variant is null)
        {
            return Result<StockAdjustmentResult>.NotFound("The product variant does not exist in the selected workspace.");
        }

        var product = await dbContext.Products.SingleOrDefaultAsync(candidate => candidate.Id == variant.ProductId, cancellationToken);
        if (product is null)
        {
            return Result<StockAdjustmentResult>.NotFound("The product variant does not exist in the selected workspace.");
        }

        if (product.PublishState == ProductPublishState.Archived)
        {
            return Result<StockAdjustmentResult>.ValidationError("Archived products cannot receive new stock adjustments.");
        }

        var item = await dbContext.InventoryItems.SingleOrDefaultAsync(candidate => candidate.VariantId == variant.Id, cancellationToken);
        if (request.Type == StockMovementType.OpeningBalance && item is not null)
        {
            return Result<StockAdjustmentResult>.ValidationError("Opening balance can only be recorded once for a variant.");
        }

        if (item is null)
        {
            if (quantityDelta < 0)
            {
                return Result<StockAdjustmentResult>.ValidationError("Stock cannot be reduced before a tracked inventory balance exists.");
            }

            item = InventoryItem.Create(context.TenantId, variant.Id);
            dbContext.InventoryItems.Add(item);
        }

        item.ApplyMovement(quantityDelta);
        var actorUserId = context.UserId ?? throw new InvalidOperationException("Stock adjustments require an authenticated actor.");
        var movement = StockMovement.Create(
            context.TenantId,
            item.Id,
            variant.Id,
            request.Type,
            quantityDelta,
            request.Reason,
            actorUserId,
            request.IdempotencyKey,
            fingerprint);
        dbContext.StockMovements.Add(movement);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditEvents.AppendAsync(new AuditEventWrite(
            "inventory.stock.adjusted", "stock-movement", movement.Id,
            Metadata: $"{{\"inventoryItemId\":\"{item.Id}\",\"variantId\":\"{variant.Id}\",\"type\":\"{movement.Type}\",\"quantity\":{Math.Abs(movement.QuantityDelta)}}}"), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result<StockAdjustmentResult>.Success(new StockAdjustmentResult(Map(item), Map(movement), false));
    }

    private InventoryBalance Map(InventoryItem item) => new(
        item.Id,
        item.TenantId,
        item.VariantId,
        item.OnHandQuantity,
        item.ReservedQuantity,
        item.AvailableQuantity,
        item.LowStockThreshold,
        item.IsLowStock,
        dbContext.Entry(item).Property<uint>("xmin").CurrentValue);

    private static InventoryStockMovement Map(StockMovement movement) => new(
        movement.Id,
        movement.InventoryItemId,
        movement.VariantId,
        movement.Type,
        movement.QuantityDelta,
        movement.Reason,
        movement.ActorUserId,
        movement.CreatedAt);

    private void SetExpectedVersion(InventoryItem item, uint expectedVersion) =>
        dbContext.Entry(item).Property<uint>("xmin").OriginalValue = expectedVersion;

    private static int GetQuantityDelta(StockMovementType type, int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Stock adjustment quantity must be greater than zero.");
        }

        return type switch
        {
            StockMovementType.OpeningBalance or StockMovementType.Receipt or StockMovementType.CorrectionIncrease => quantity,
            StockMovementType.CorrectionDecrease or StockMovementType.Damage => -quantity,
            _ => throw new ArgumentOutOfRangeException(nameof(type), "The stock adjustment type is not supported.")
        };
    }

    private static StockAdjustmentRequest Normalize(StockAdjustmentRequest request) => new(
        NormalizeRequired(request.VariantId, nameof(request.VariantId), 26),
        request.Type,
        request.Quantity,
        NormalizeRequired(request.Reason, nameof(request.Reason), StockMovement.ReasonMaxLength),
        NormalizeRequired(request.IdempotencyKey, nameof(request.IdempotencyKey), StockMovement.IdempotencyKeyMaxLength));

    private static string NormalizeRequired(string value, string parameterName, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A value is required.", parameterName)
            : value.Trim();
        return normalized.Length > maxLength
            ? throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maxLength} characters.")
            : normalized;
    }

    private static string CreateFingerprint(StockAdjustmentRequest request)
    {
        var canonical = JsonSerializer.Serialize(new { request.VariantId, request.Type, request.Quantity, request.Reason });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string EncodeCursor(DateTimeOffset createdAt, string id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{createdAt.UtcTicks}|{id}"));

    private static (DateTimeOffset CreatedAt, string Id)? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|', 2);
            return parts.Length == 2 && long.TryParse(parts[0], out var ticks) && !string.IsNullOrWhiteSpace(parts[1])
                ? (new DateTimeOffset(ticks, TimeSpan.Zero), parts[1])
                : throw new ArgumentException("The stock-movement cursor is invalid.", nameof(cursor));
        }
        catch (FormatException)
        {
            throw new ArgumentException("The stock-movement cursor is invalid.", nameof(cursor));
        }
    }

    private static bool IsValidationException(Exception exception) =>
        exception is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException;

    private static bool IsRetryable(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.UniqueViolation
        };

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
