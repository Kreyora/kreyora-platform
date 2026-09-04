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
using Kreyora.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Kreyora.Infrastructure.Inventory;

public sealed class InventoryService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    ITenantPermissionAuthorizer permissionAuthorizer,
    IAuditEventService auditEvents,
    Domain.Abstractions.ITimeProvider timeProvider,
    IOptions<InventoryReservationOptions> reservationOptions) : IInventoryService
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

    public async Task<Result<InventoryReservationResult>> ReserveStockAsync(ReserveStockRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.InventoryWrite);
        var context = tenantContext.RequireCurrent();
        try
        {
            request = Normalize(request);
            var fingerprint = Fingerprint(new { request.VariantId, request.Quantity, request.Source, request.ReferenceId });
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var replay = await FindReplayAsync(InventoryReservationCommandOperation.Reserve, request.IdempotencyKey, fingerprint, cancellationToken);
            if (replay is not null) return replay;

            var item = await LockInventoryItemForVariantAsync(request.VariantId, cancellationToken);
            if (item is null) return Result<InventoryReservationResult>.NotFound("No tracked inventory exists for this variant in the selected workspace.");
            await ExpireDueForItemAsync(item, cancellationToken);
            item.Reserve(request.Quantity);
            var actor = context.UserId ?? throw new InvalidOperationException("Stock reservations require an authenticated actor.");
            var now = timeProvider.UtcNow;
            var reservation = InventoryReservation.Create(context.TenantId, item.Id, item.VariantId, request.Quantity, request.Source,
                request.ReferenceId, actor, now.Add(reservationOptions.Value.DefaultDuration), now);
            dbContext.InventoryReservations.Add(reservation);
            dbContext.InventoryReservationCommands.Add(InventoryReservationCommand.Create(context.TenantId, reservation.Id,
                InventoryReservationCommandOperation.Reserve, request.IdempotencyKey, fingerprint));
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditEvents.AppendAsync(new AuditEventWrite("inventory.reservation.created", "inventory-reservation", reservation.Id,
                Metadata: $"{{\"inventoryItemId\":\"{item.Id}\",\"variantId\":\"{item.VariantId}\",\"quantity\":{reservation.Quantity},\"source\":\"{reservation.Source}\"}}"), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<InventoryReservationResult>.Success(new InventoryReservationResult(Map(reservation), Map(item), null, false));
        }
        catch (Exception exception) when (IsValidationException(exception))
        {
            dbContext.ChangeTracker.Clear();
            return Result<InventoryReservationResult>.ValidationError(exception.Message);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Result<InventoryReservationResult>.Conflict("The stock reservation conflicted with another update. Please retry.");
        }
        catch (DbUpdateException)
        {
            return Result<InventoryReservationResult>.Conflict("The stock reservation conflicted with another update. Please retry.");
        }
    }

    public Task<Result<InventoryReservationResult>> CommitReservationAsync(ReservationTransitionRequest request, CancellationToken cancellationToken = default) =>
        TransitionReservationAsync(request, InventoryReservationCommandOperation.Commit, cancellationToken);

    public Task<Result<InventoryReservationResult>> ReleaseReservationAsync(ReservationTransitionRequest request, CancellationToken cancellationToken = default) =>
        TransitionReservationAsync(request, InventoryReservationCommandOperation.Release, cancellationToken);

    public async Task<Result<InventoryReservationPage>> GetReservationsAsync(string variantId, InventoryReservationState? state, string? cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.InventoryRead);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = dbContext.InventoryReservations.AsNoTracking().Where(item => item.VariantId == variantId);
        if (state is not null) query = query.Where(item => item.State == state);
        var items = await query.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id).Take(pageSize).ToListAsync(cancellationToken);
        return Result<InventoryReservationPage>.Success(new InventoryReservationPage(items.Select(Map).ToArray(), null));
    }

    public async Task<int> ExpireDueReservationsAsync(CancellationToken cancellationToken = default)
    {
        var context = tenantContext.RequireCurrent();
        var now = timeProvider.UtcNow;
        var due = await dbContext.InventoryReservations.Where(item => item.State == InventoryReservationState.Active && item.ExpiresAt <= now)
            .OrderBy(item => item.ExpiresAt).ThenBy(item => item.Id).Take(reservationOptions.Value.ExpiryBatchSize).ToListAsync(cancellationToken);
        var count = 0;
        foreach (var reservation in due)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var current = await dbContext.InventoryReservations.SingleOrDefaultAsync(item => item.Id == reservation.Id, cancellationToken);
            if (current is null || current.State != InventoryReservationState.Active || current.ExpiresAt > timeProvider.UtcNow) continue;
            var item = await dbContext.InventoryItems.SingleAsync(item => item.Id == current.InventoryItemId, cancellationToken);
            item.ReleaseReservation(current.Quantity);
            current.Expire(timeProvider.UtcNow);
            AddExpiryCommand(context.TenantId, current.Id);
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditEvents.AppendAsync(new AuditEventWrite("inventory.reservation.expired", "inventory-reservation", current.Id,
                Metadata: $"{{\"inventoryItemId\":\"{item.Id}\",\"quantity\":{current.Quantity},\"automated\":true}}", ActorUserId: current.ActorUserId), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            count++;
        }
        return count;
    }

    private async Task<Result<InventoryReservationResult>> TransitionReservationAsync(ReservationTransitionRequest request, InventoryReservationCommandOperation operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.InventoryWrite);
        var context = tenantContext.RequireCurrent();
        try
        {
            request = Normalize(request);
            var fingerprint = Fingerprint(new { request.ReservationId });
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var replay = await FindReplayAsync(operation, request.IdempotencyKey, fingerprint, cancellationToken);
            if (replay is not null) return replay;
            var reservation = await dbContext.InventoryReservations.SingleOrDefaultAsync(item => item.Id == request.ReservationId, cancellationToken);
            if (reservation is null) return Result<InventoryReservationResult>.NotFound("The reservation does not exist in the selected workspace.");
            var item = await LockInventoryItemAsync(reservation.InventoryItemId, cancellationToken);
            var now = timeProvider.UtcNow;
            if (reservation.State != InventoryReservationState.Active) return Result<InventoryReservationResult>.Conflict("The reservation has already reached a terminal state.");
            if (reservation.ExpiresAt <= now)
            {
                item.ReleaseReservation(reservation.Quantity);
                reservation.Expire(now);
                AddExpiryCommand(context.TenantId, reservation.Id);
                await dbContext.SaveChangesAsync(cancellationToken);
                await auditEvents.AppendAsync(new AuditEventWrite("inventory.reservation.expired", "inventory-reservation", reservation.Id,
                    Metadata: $"{{\"inventoryItemId\":\"{item.Id}\",\"quantity\":{reservation.Quantity},\"automated\":true}}", ActorUserId: reservation.ActorUserId), cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Result<InventoryReservationResult>.Conflict("The reservation has expired.");
            }

            StockMovement? movement = null;
            if (operation == InventoryReservationCommandOperation.Commit)
            {
                item.CommitReservation(reservation.Quantity);
                reservation.Commit(now);
                movement = StockMovement.Create(context.TenantId, item.Id, item.VariantId, StockMovementType.ReservationCommitted,
                    -reservation.Quantity, "Reservation committed", context.UserId ?? throw new InvalidOperationException("An actor is required."),
                    $"reservation:commit:{reservation.Id}", Fingerprint(new { reservation.Id, operation }), "reservation", reservation.Id);
                dbContext.StockMovements.Add(movement);
            }
            else
            {
                item.ReleaseReservation(reservation.Quantity);
                reservation.Release(now);
            }

            dbContext.InventoryReservationCommands.Add(InventoryReservationCommand.Create(context.TenantId, reservation.Id, operation, request.IdempotencyKey, fingerprint));
            await dbContext.SaveChangesAsync(cancellationToken);
            var action = operation == InventoryReservationCommandOperation.Commit ? "inventory.reservation.committed" : "inventory.reservation.released";
            await auditEvents.AppendAsync(new AuditEventWrite(action, "inventory-reservation", reservation.Id,
                Metadata: $"{{\"inventoryItemId\":\"{item.Id}\",\"quantity\":{reservation.Quantity}}}"), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<InventoryReservationResult>.Success(new InventoryReservationResult(Map(reservation), Map(item), movement is null ? null : Map(movement), false));
        }
        catch (Exception exception) when (IsValidationException(exception))
        {
            dbContext.ChangeTracker.Clear();
            return Result<InventoryReservationResult>.ValidationError(exception.Message);
        }
        catch (DbUpdateException)
        {
            return Result<InventoryReservationResult>.Conflict("The reservation transition conflicted with another update. Please retry.");
        }
    }

    private async Task<Result<InventoryReservationResult>?> FindReplayAsync(InventoryReservationCommandOperation operation, string idempotencyKey, string fingerprint, CancellationToken cancellationToken)
    {
        var command = await dbContext.InventoryReservationCommands.SingleOrDefaultAsync(item => item.Operation == operation && item.IdempotencyKey == idempotencyKey, cancellationToken);
        if (command is null) return null;
        if (!string.Equals(command.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            return Result<InventoryReservationResult>.Conflict("The idempotency key was already used for a different reservation command.");
        var reservation = await dbContext.InventoryReservations.SingleAsync(item => item.Id == command.ReservationId, cancellationToken);
        var item = await dbContext.InventoryItems.SingleAsync(item => item.Id == reservation.InventoryItemId, cancellationToken);
        var movement = operation == InventoryReservationCommandOperation.Commit
            ? await dbContext.StockMovements.SingleOrDefaultAsync(item => item.ReferenceType == "reservation" && item.ReferenceId == reservation.Id, cancellationToken)
            : null;
        return Result<InventoryReservationResult>.Success(new InventoryReservationResult(Map(reservation), Map(item), movement is null ? null : Map(movement), true));
    }

    private async Task ExpireDueForItemAsync(InventoryItem item, CancellationToken cancellationToken)
    {
        var now = timeProvider.UtcNow;
        var due = await dbContext.InventoryReservations.Where(reservation => reservation.InventoryItemId == item.Id && reservation.State == InventoryReservationState.Active && reservation.ExpiresAt <= now).ToListAsync(cancellationToken);
        foreach (var reservation in due)
        {
            item.ReleaseReservation(reservation.Quantity);
            reservation.Expire(now);
            AddExpiryCommand(tenantContext.RequireCurrent().TenantId, reservation.Id);
            await auditEvents.AppendAsync(new AuditEventWrite("inventory.reservation.expired", "inventory-reservation", reservation.Id,
                Metadata: $"{{\"inventoryItemId\":\"{item.Id}\",\"quantity\":{reservation.Quantity},\"automated\":true}}", ActorUserId: reservation.ActorUserId), cancellationToken);
        }
    }

    private static ReserveStockRequest Normalize(ReserveStockRequest request) => new(NormalizeRequired(request.VariantId, nameof(request.VariantId), 26), request.Quantity,
        Enum.IsDefined(request.Source) ? request.Source : throw new ArgumentOutOfRangeException(nameof(request)), NormalizeRequired(request.ReferenceId, nameof(request.ReferenceId), InventoryReservation.ReferenceIdMaxLength), NormalizeRequired(request.IdempotencyKey, nameof(request.IdempotencyKey), 256));

    private static ReservationTransitionRequest Normalize(ReservationTransitionRequest request) => new(NormalizeRequired(request.ReservationId, nameof(request.ReservationId), 26), NormalizeRequired(request.IdempotencyKey, nameof(request.IdempotencyKey), 256));

    private static string Fingerprint<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

    private static InventoryReservationItem Map(InventoryReservation item) => new(item.Id, item.InventoryItemId, item.VariantId, item.Quantity, item.Source, item.ReferenceId, item.State, item.ExpiresAt, item.CommittedAt, item.ReleasedAt, item.ExpiredAt);

    private void AddExpiryCommand(string tenantId, string reservationId)
    {
        dbContext.InventoryReservationCommands.Add(InventoryReservationCommand.Create(
            tenantId,
            reservationId,
            InventoryReservationCommandOperation.Expire,
            $"expiry:{reservationId}",
            Fingerprint(new { reservationId })));
    }

    private Task<InventoryItem?> LockInventoryItemForVariantAsync(string variantId, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.RequireCurrent().TenantId;
        return dbContext.InventoryItems.FromSqlInterpolated($"SELECT k.*, k.xmin FROM inventory_items k WHERE k.tenant_id = {tenantId} AND k.variant_id = {variantId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<InventoryItem> LockInventoryItemAsync(string inventoryItemId, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.RequireCurrent().TenantId;
        return await dbContext.InventoryItems.FromSqlInterpolated($"SELECT k.*, k.xmin FROM inventory_items k WHERE k.tenant_id = {tenantId} AND k.id = {inventoryItemId} FOR UPDATE")
            .SingleAsync(cancellationToken);
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
