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

public sealed class DeliveryRuleService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    ITenantPermissionAuthorizer permissionAuthorizer,
    IAuditEventService auditEvents) : IDeliveryRuleService, IDeliveryRuleReadService
{
    private const string CreateOperation = "delivery-rule.create";

    public async Task<Result<DeliveryRuleItem>> GetAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.StorefrontRead);
        var rule = await FindRuleAsync(ruleId, cancellationToken);
        return rule is null ? Result<DeliveryRuleItem>.NotFound("The delivery rule was not found.") : Result<DeliveryRuleItem>.Success(Map(rule));
    }

    public async Task<Result<DeliveryRulePage>> ListAsync(DeliveryRuleQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        permissionAuthorizer.Demand(TenantPermissions.StorefrontRead);
        var store = await GetStoreAsync(cancellationToken);
        if (store is null) return Result<DeliveryRulePage>.NotFound("A store has not been created for the selected workspace.");
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var rules = dbContext.DeliveryRules.Where(rule => rule.StoreId == store.Id).Include(rule => rule.Zones)
            .OrderBy(rule => rule.Priority).ThenBy(rule => rule.Name);
        var total = await rules.CountAsync(cancellationToken);
        var items = await rules.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return Result<DeliveryRulePage>.Success(new DeliveryRulePage(items.Select(Map).ToArray(), page, pageSize, total));
    }

    public async Task<Result<DeliveryRuleItem>> CreateAsync(CreateDeliveryRuleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.StorefrontWrite);
        var context = tenantContext.RequireCurrent();
        var fingerprint = Fingerprint(request.Rule);
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var prior = await FindCommandAsync(request.IdempotencyKey, cancellationToken);
            if (prior is not null)
            {
                if (!string.Equals(prior.RequestFingerprint, fingerprint, StringComparison.Ordinal)) return Result<DeliveryRuleItem>.Conflict("The idempotency key was already used for a different delivery rule request.");
                var replay = await FindRuleAsync(prior.ResourceId, cancellationToken);
                return replay is null ? Result<DeliveryRuleItem>.Conflict("The original delivery rule operation is incomplete.") : Result<DeliveryRuleItem>.Success(Map(replay));
            }

            var store = await GetStoreAsync(cancellationToken);
            if (store is null) return Result<DeliveryRuleItem>.NotFound("A store has not been created for the selected workspace.");
            var rule = DeliveryRule.Create(context.TenantId, store.Id, ToSettings(request.Rule));
            dbContext.DeliveryRules.Add(rule);
            dbContext.StoreCommandIdempotencyRecords.Add(StoreCommandIdempotency.Create(context.TenantId, CreateOperation, request.IdempotencyKey, fingerprint, rule.Id));
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditEvents.AppendAsync(new AuditEventWrite("delivery-rule.created", "delivery-rule", rule.Id, Metadata: Metadata(rule)), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<DeliveryRuleItem>.Success(Map(rule));
        }
        catch (Exception exception) when (IsValidationException(exception))
        {
            return Result<DeliveryRuleItem>.ValidationError(exception.Message);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Result<DeliveryRuleItem>.Conflict("A delivery rule with the same coverage already exists.");
        }
    }

    public async Task<Result<DeliveryRuleItem>> UpdateAsync(UpdateDeliveryRuleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.StorefrontWrite);
        var rule = await FindRuleAsync(request.RuleId, cancellationToken);
        if (rule is null) return Result<DeliveryRuleItem>.NotFound("The delivery rule was not found.");
        try
        {
            dbContext.Entry(rule).Property<uint>("xmin").OriginalValue = request.ExpectedVersion;
            var wasActive = rule.IsActive;
            rule.Update(ToSettings(request.Rule));
            await dbContext.SaveChangesAsync(cancellationToken);
            var action = rule.IsActive == wasActive ? "delivery-rule.updated" : rule.IsActive ? "delivery-rule.activated" : "delivery-rule.deactivated";
            await auditEvents.AppendAsync(new AuditEventWrite(action, "delivery-rule", rule.Id, Metadata: Metadata(rule)), cancellationToken);
            return Result<DeliveryRuleItem>.Success(Map(rule));
        }
        catch (Exception exception) when (IsValidationException(exception))
        {
            return Result<DeliveryRuleItem>.ValidationError(exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<DeliveryRuleItem>.Conflict("The delivery rule was changed by another user. Refresh and try again.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Result<DeliveryRuleItem>.Conflict("A delivery rule with the same coverage already exists.");
        }
    }

    public Task<bool> HasActiveRulesAsync(string storeId, CancellationToken cancellationToken = default) =>
        dbContext.DeliveryRules.AnyAsync(rule => rule.StoreId == storeId && rule.IsActive && rule.Zones.Any(), cancellationToken);

    private Task<Store?> GetStoreAsync(CancellationToken cancellationToken) => dbContext.Stores.SingleOrDefaultAsync(cancellationToken);
    private Task<DeliveryRule?> FindRuleAsync(string ruleId, CancellationToken cancellationToken) =>
        dbContext.DeliveryRules.Include(rule => rule.Zones).SingleOrDefaultAsync(rule => rule.Id == ruleId, cancellationToken);
    private Task<StoreCommandIdempotency?> FindCommandAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        dbContext.StoreCommandIdempotencyRecords.SingleOrDefaultAsync(record => record.Operation == CreateOperation && record.IdempotencyKey == idempotencyKey, cancellationToken);

    private static DeliveryRuleSettings ToSettings(DeliveryRuleInput input) => new(input.Name, input.Priority, input.FeeType, input.BaseFeeNpr, input.FreeAboveNpr,
        input.EstimatedEtaText, input.CodAvailable, input.IsActive, input.Zones);
    private DeliveryRuleItem Map(DeliveryRule rule) => new(rule.Id, rule.Name, rule.Priority, rule.FeeType, rule.BaseFeeNpr, rule.FreeAboveNpr,
        rule.EstimatedEtaText, rule.CodAvailable, rule.IsActive, rule.Zones.Select(zone => new DeliveryRuleZoneItem(zone.District, zone.Municipality, zone.Locality)).ToArray(),
        dbContext.Entry(rule).Property<uint>("xmin").CurrentValue);
    private static string Metadata(DeliveryRule rule) => $"{{\"isActive\":{rule.IsActive.ToString().ToLowerInvariant()},\"codAvailable\":{rule.CodAvailable.ToString().ToLowerInvariant()}}}";
    private static string Fingerprint(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static bool IsValidationException(Exception exception) => exception is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException;
}
