using System.Text.Json;
using Kreyora.Application.Assistant;
using Kreyora.Application.Audit;
using Kreyora.Application.Inventory;
using Kreyora.Application.Models;
using Kreyora.Application.Storefront;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Assistant;
using Kreyora.Domain.Common;
using Kreyora.Domain.Conversations;
using Kreyora.Domain.Storefront;
using Kreyora.Infrastructure.Ai;
using Kreyora.Infrastructure.Assistant.Tools;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Assistant;

// M09-S05 write services (ADR-020). Every rule about products, prices, stock, checkout and takeover stays in the
// existing commands these services call; what lives here is the assistant-specific policy: limits, the two-phase
// confirmation, link lifecycle and replay.

/// <summary>Completed write calls by idempotency key, so a duplicate call returns the first result.</summary>
public sealed class AssistantActionStore(AppDbContext dbContext, ITimeProvider timeProvider) : IAssistantActionStore
{
    public async Task<string?> FindCompletedAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        await dbContext.AssistantActions.AsNoTracking()
            .Where(a => a.IdempotencyKey == idempotencyKey && a.Status == AssistantActionStatus.Completed)
            .Select(a => a.ResultJson).FirstOrDefaultAsync(cancellationToken);

    public async Task SaveCompletedAsync(AssistantToolContext context, string tool, AssistantCallContext callContext, string argumentsFingerprint, string resultJson, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(callContext);
        dbContext.AssistantActions.Add(AssistantAction.Completed(callContext.ActionId, context.TenantId, context.ConversationId!, tool, callContext.IdempotencyKey,
            argumentsFingerprint, resultJson, callContext.InternalReference, callContext.ReferenceExpiresAt, timeProvider.UtcNow));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear(); // a concurrent duplicate stored it first; its result stands
        }
    }
}

/// <summary>One validated cart line: published, visible in this store, enough stock.</summary>
public sealed record ValidatedCartLine(string VariantId, string ProductId, string ProductTitle, string VariantName, int Quantity, decimal UnitPriceNpr);

/// <summary>The same purchasability rules the storefront applies (publication, visibility, available stock).</summary>
public sealed class AssistantCartValidator(AppDbContext dbContext, IStorefrontCatalogReadService catalog, IStorefrontInventoryReadService inventory, IConversationHoldAllowance holds)
{
    /// <param name="conversationId">Units this chat already holds count as available to it.</param>
    public async Task<(IReadOnlyList<ValidatedCartLine>? Lines, string? Code, string? Message)> ValidateAsync(
        string storeId, string? conversationId, IReadOnlyList<AssistantCartLine> items, int maxLines, int maxQuantity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0 || items.Count > maxLines) return (null, AssistantToolErrorCodes.LimitReached, $"Use 1-{maxLines} different items.");
        if (items.Any(i => i.Quantity < 1 || i.Quantity > maxQuantity)) return (null, AssistantToolErrorCodes.LimitReached, $"Quantities must be between 1 and {maxQuantity}; hand larger orders to a team member.");
        if (items.GroupBy(i => i.VariantId, StringComparer.Ordinal).Any(g => g.Count() > 1)) return (null, AssistantToolErrorCodes.InvalidArguments, "List each item once.");

        var held = conversationId is null ? new Dictionary<string, int>() : await holds.HeldAsync(storeId, conversationId, null, cancellationToken);
        var lines = new List<ValidatedCartLine>();
        foreach (var item in items.OrderBy(i => i.VariantId, StringComparer.Ordinal))
        {
            var variant = await catalog.GetPublishedVariantAsync(item.VariantId, cancellationToken);
            var visible = variant is not null && await dbContext.StoreProductPublications.AnyAsync(p =>
                p.StoreId == storeId && p.ProductId == variant.ProductId && p.Visibility == StoreProductVisibility.Visible, cancellationToken);
            if (variant is null || !visible) return (null, AssistantToolErrorCodes.NotFound, "An item is not available in this shop. Search again.");
            var available = (await inventory.GetAvailableQuantityAsync(item.VariantId, cancellationToken) ?? 0) + held.GetValueOrDefault(item.VariantId);
            if (available < item.Quantity) return (null, AssistantToolErrorCodes.NotFound, "Not enough stock for an item. Check stock again and tell the customer.");
            lines.Add(new ValidatedCartLine(variant.VariantId, variant.ProductId, variant.ProductTitle, variant.VariantName, item.Quantity, variant.UnitPriceNpr));
        }

        return (lines, null, null);
    }
}

/// <summary>QuoteCart: place → zone destination → the storefront quote service. Writes nothing but the replay record.</summary>
public sealed class AssistantQuoteService(
    IDeliveryInfoQuery delivery,
    IStorefrontQuoteService quotes,
    AssistantCallContext callContext) : IAssistantQuoteService
{
    public const int MaxLines = 10;
    public const int MaxQuantity = 5;

    public async Task<AssistantWriteOutcome<AssistantQuote>> QuoteAsync(AssistantToolContext context, string place, IReadOnlyList<AssistantCartLine> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count is 0 or > MaxLines || items.Any(i => i.Quantity is < 1 or > MaxQuantity))
            return AssistantWriteOutcome.Fail<AssistantQuote>(AssistantToolErrorCodes.LimitReached, $"Quote 1-{MaxLines} items with 1-{MaxQuantity} units each.");

        var resolution = await delivery.ResolveDestinationAsync(context.StoreId, place, cancellationToken);
        if (resolution.Destination is null)
        {
            return resolution.Status switch
            {
                DeliveryInfoStatus.NeedsMoreDetail => AssistantWriteOutcome.Fail<AssistantQuote>(AssistantToolErrorCodes.NeedsMoreDetail, "Ask the customer which of these places they mean.", new { options = resolution.Suggestions }),
                DeliveryInfoStatus.PlaceNotServed => AssistantWriteOutcome.Fail<AssistantQuote>(AssistantToolErrorCodes.PlaceNotServed, "The shop does not deliver there.", new { servedPlaces = resolution.Suggestions }),
                _ => AssistantWriteOutcome.Fail<AssistantQuote>(AssistantToolErrorCodes.PlaceUnknown, "The place was not recognised. Ask for the district or city.", new { servedPlaces = resolution.Suggestions })
            };
        }

        var quote = await quotes.CreateQuoteAsync(new StorefrontQuoteRequest([.. items.Select(i => new StorefrontQuoteLineRequest(i.VariantId, i.Quantity))], resolution.Destination,
            HeldByConversationId: context.ConversationId), cancellationToken);
        if (quote.IsFailure)
        {
            return AssistantWriteOutcome.Fail<AssistantQuote>(AssistantToolErrorCodes.NotFound, "An item is unavailable or out of stock. Check the items again.");
        }

        var value = quote.Value!;
        string? quoteId = null;
        if (!context.IsSellerPreview)
        {
            // The signed quote stays on the server; the model gets an ID it can pass to CreateCheckoutLink.
            callContext.InternalReference = value.QuoteToken;
            callContext.ReferenceExpiresAt = value.ExpiresAt;
            quoteId = callContext.ActionId;
        }

        return AssistantWriteOutcome.Ok(new AssistantQuote(quoteId, resolution.Label ?? place,
            [.. value.Lines.Select(l => new AssistantQuoteLine(l.VariantId, l.ProductTitle, l.VariantName, l.Quantity, l.UnitPriceNpr, l.LineSubtotalNpr))],
            value.Totals.MerchandiseSubtotalNpr, value.Totals.DeliveryFeeNpr, value.Totals.TotalNpr, value.Delivery.EstimatedEtaText, value.Delivery.CodAvailable, value.ExpiresAt));
    }
}

/// <summary>ReserveInventory / ReleaseReservation: bounded chat holds, two-phase confirmation checked by the server.</summary>
public sealed class AssistantHoldService(
    AppDbContext dbContext,
    AssistantCartValidator validator,
    IConversationInventoryHoldService holds,
    IOptionsMonitor<AiOptions> aiOptions,
    ITimeProvider timeProvider) : IAssistantHoldService
{
    public const int MaxLines = 5;
    public const int MaxQuantity = 5;
    public const string Tool = "ReserveInventory";

    public async Task<AssistantWriteOutcome<AssistantHoldResult>> ReserveAsync(AssistantToolContext context, IReadOnlyList<AssistantCartLine> items, string? confirmationId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var (lines, code, message) = await validator.ValidateAsync(context.StoreId, context.ConversationId, items, MaxLines, MaxQuantity, cancellationToken);
        if (lines is null) return AssistantWriteOutcome.Fail<AssistantHoldResult>(code!, message!);

        var now = timeProvider.UtcNow;
        var fingerprint = AssistantCheckoutLink.Fingerprint(lines.Select(l => new CheckoutLinkLine(l.VariantId, l.Quantity)));
        var cartLines = lines.Select(l => new AssistantCartLine(l.VariantId, l.Quantity)).ToList();
        if (context.IsSellerPreview)
        {
            return AssistantWriteOutcome.Ok(new AssistantHoldResult(new AssistantHoldProposal("dry-run", cartLines, now.AddMinutes(aiOptions.CurrentValue.Tools.ConfirmationMinutes)), [], DryRun: true));
        }

        var conversationId = context.ConversationId!;
        var cap = aiOptions.CurrentValue.Tools.MaxHoldsPerConversationPerDay;
        var active = await holds.GetActiveAsync(conversationId, cancellationToken);
        var newLines = lines.Count(l => active.All(h => h.VariantId != l.VariantId));
        if (await holds.CountCreatedSinceAsync(conversationId, now.AddHours(-24), cancellationToken) + newLines > cap)
        {
            return AssistantWriteOutcome.Fail<AssistantHoldResult>(AssistantToolErrorCodes.LimitReached, "This chat has reached today's hold limit. Send a checkout link instead or hand over to a team member.");
        }

        if (confirmationId is null)
        {
            // Phase 1: propose. Nothing is held until the customer has seen this summary and replied.
            var proposal = AssistantAction.Propose(context.TenantId, conversationId, Tool, $"proposal:{IdGenerator.NewId()}", fingerprint, now.AddMinutes(aiOptions.CurrentValue.Tools.ConfirmationMinutes));
            dbContext.AssistantActions.Add(proposal);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Proposed(proposal, cartLines);
        }

        var pending = await dbContext.AssistantActions.SingleOrDefaultAsync(a => a.Id == confirmationId && a.ConversationId == conversationId && a.Tool == Tool, cancellationToken);
        if (pending is null) return AssistantWriteOutcome.Fail<AssistantHoldResult>(AssistantToolErrorCodes.NotFound, "That confirmation does not exist. Propose the hold again.");
        if (!pending.CanConfirm(conversationId, Tool, fingerprint, now))
        {
            return AssistantWriteOutcome.Fail<AssistantHoldResult>(AssistantToolErrorCodes.Stale,
                pending.ArgumentsFingerprint != fingerprint ? "The items differ from what the customer confirmed. Propose again." : "The confirmation expired or was used. Propose again.");
        }

        // Phase 2 requires a customer message after the proposal (server-checked; the model's word is not enough).
        var replied = await dbContext.Messages.AnyAsync(m => m.ConversationId == conversationId && m.Direction == MessageDirection.Inbound &&
            m.Origin == MessageOrigin.Customer && m.ReceivedAt > pending.CreatedAt, cancellationToken);
        if (!replied) return Proposed(pending, cartLines);

        var held = await holds.HoldAsync(new ConversationHoldRequest(conversationId, [.. lines.Select(l => new ConversationHoldLine(l.VariantId, l.Quantity))], $"hold:{pending.Id}"), cancellationToken);
        if (held.IsFailure) return AssistantWriteOutcome.Fail<AssistantHoldResult>(AssistantToolErrorCodes.NotFound, "Not enough stock to hold now. Tell the customer and offer a checkout link.");

        pending.Confirm(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return AssistantWriteOutcome.Ok(new AssistantHoldResult(null, [.. held.Value!.Select(h => new AssistantHold(h.ReservationId, h.VariantId, h.Quantity, h.ExpiresAt))], DryRun: false));
    }

    public async Task<AssistantWriteOutcome<IReadOnlyList<AssistantHold>>> ReleaseAsync(AssistantToolContext context, IReadOnlyList<string>? reservationIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.IsSellerPreview) return AssistantWriteOutcome.Ok<IReadOnlyList<AssistantHold>>([]);
        var released = await holds.ReleaseAsync(context.ConversationId!, reservationIds, cancellationToken);
        return released.IsFailure
            ? AssistantWriteOutcome.Fail<IReadOnlyList<AssistantHold>>(AssistantToolErrorCodes.NotFound, "That hold is not active in this chat.")
            : AssistantWriteOutcome.Ok<IReadOnlyList<AssistantHold>>([.. released.Value!.Select(h => new AssistantHold(h.ReservationId, h.VariantId, h.Quantity, h.ExpiresAt))]);
    }

    private static AssistantWriteOutcome<AssistantHoldResult> Proposed(AssistantAction proposal, List<AssistantCartLine> lines) =>
        AssistantWriteOutcome.Fail<AssistantHoldResult>(AssistantToolErrorCodes.ConfirmationRequired,
            "Show the customer this summary and ask them to confirm. After they reply yes, call ReserveInventory again with the same items and this confirmationId.",
            new { confirmationId = proposal.Id, items = lines, expiresAt = proposal.ExpiresAt });
}

/// <summary>CreateCheckoutLink, the storefront's public read of a link, and the checkout handover hooks.</summary>
public sealed class AssistantCheckoutLinkService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    IPublicStorefrontContextAccessor publicContext,
    AssistantCartValidator validator,
    IStorefrontQuoteService quotes,
    IStorefrontCatalogReadService catalog,
    IStorefrontInventoryReadService inventory,
    ICheckoutInventoryReservationService checkoutInventory,
    IConversationHoldAllowance holdAllowance,
    IAuditEventService auditEvents,
    IOptionsMonitor<AiOptions> aiOptions,
    IOptions<StorefrontLinkOptions> linkOptions,
    ITimeProvider timeProvider) : IAssistantCheckoutLinkService, IAssistantCheckoutLinkHandover
{
    public async Task<AssistantWriteOutcome<AssistantCheckoutLinkCreated>> CreateAsync(AssistantToolContext context, IReadOnlyList<AssistantCartLine> items, string? quoteId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var (lines, code, message) = await validator.ValidateAsync(context.StoreId, context.ConversationId, items, AssistantCheckoutLink.MaxLines, AssistantCheckoutLink.MaxQuantity, cancellationToken);
        if (lines is null) return AssistantWriteOutcome.Fail<AssistantCheckoutLinkCreated>(code!, message!);
        var linkLines = lines.Select(l => new CheckoutLinkLine(l.VariantId, l.Quantity)).ToList();
        var cartLines = lines.Select(l => new AssistantCartLine(l.VariantId, l.Quantity)).ToList();
        var now = timeProvider.UtcNow;
        var expiresAt = now.AddHours(aiOptions.CurrentValue.Tools.CheckoutLinkHours);

        if (quoteId is not null && !context.IsSellerPreview)
        {
            // Expected version: the link must match what was quoted, at the prices that are still current.
            var quoted = await dbContext.AssistantActions.AsNoTracking().SingleOrDefaultAsync(a => a.Id == quoteId && a.Tool == "QuoteCart" && a.ConversationId == context.ConversationId, cancellationToken);
            if (quoted?.InternalReference is null) return AssistantWriteOutcome.Fail<AssistantCheckoutLinkCreated>(AssistantToolErrorCodes.NotFound, "That quote does not exist in this chat. Quote again.");
            var current = await quotes.RevalidateForCheckoutAsync(quoted.InternalReference, cancellationToken);
            if (current.IsFailure) return AssistantWriteOutcome.Fail<AssistantCheckoutLinkCreated>(AssistantToolErrorCodes.Stale, "The quote expired or prices/stock changed. Quote again and show the customer the new total.");
            var quotedLines = current.Value!.Lines.Select(l => new CheckoutLinkLine(l.VariantId, l.Quantity));
            if (AssistantCheckoutLink.Fingerprint(quotedLines) != AssistantCheckoutLink.Fingerprint(linkLines))
                return AssistantWriteOutcome.Fail<AssistantCheckoutLinkCreated>(AssistantToolErrorCodes.Stale, "The items differ from the quote. Quote again or use the quoted items.");
        }

        if (context.IsSellerPreview)
        {
            return AssistantWriteOutcome.Ok(new AssistantCheckoutLinkCreated(null, expiresAt, cartLines, Reused: false, DryRun: true));
        }

        var conversationId = context.ConversationId!;
        var live = await dbContext.AssistantCheckoutLinks.CountAsync(l => l.ConversationId == conversationId && l.State == AssistantCheckoutLinkState.Active && l.ExpiresAt > now, cancellationToken);
        if (live >= aiOptions.CurrentValue.Tools.MaxLiveLinksPerConversation)
        {
            return AssistantWriteOutcome.Fail<AssistantCheckoutLinkCreated>(AssistantToolErrorCodes.LimitReached, "This chat already has several open checkout links. Point the customer to the latest one or hand over to a team member.");
        }

        var slug = await dbContext.Stores.AsNoTracking().Where(s => s.Id == context.StoreId).Select(s => s.PlatformSlug).SingleAsync(cancellationToken);
        var (link, token) = AssistantCheckoutLink.Create(context.TenantId, context.StoreId, conversationId, context.CustomerChannelIdentityId!, linkLines, expiresAt, now);
        dbContext.AssistantCheckoutLinks.Add(link);
        await auditEvents.AppendAsync(new AuditEventWrite("assistant.checkout_link.created", "assistant-checkout-link", link.Id,
            Metadata: JsonSerializer.Serialize(new { conversationId, lines = linkLines.Count, units = linkLines.Sum(l => l.Quantity), expiresAt }),
            ActorKind: CommerceActorKind.CommerceSystem), cancellationToken);
        return AssistantWriteOutcome.Ok(new AssistantCheckoutLinkCreated(Url(slug, token), expiresAt, cartLines, Reused: false, DryRun: false));
    }

    public async Task<Result<PublicAssistantLink>> GetPublicAsync(string token, CancellationToken cancellationToken = default)
    {
        var store = publicContext.RequireCurrent();
        var hash = AssistantCheckoutLink.HashToken(token);
        var link = await dbContext.AssistantCheckoutLinks.AsNoTracking().SingleOrDefaultAsync(l => l.TokenHash == hash && l.StoreId == store.StoreId, cancellationToken);
        if (link is null || !link.IsLive(timeProvider.UtcNow)) return Result<PublicAssistantLink>.NotFound("This checkout link has expired. Ask the shop for a new one.");

        var held = await holdAllowance.HeldAsync(store.StoreId, link.ConversationId, null, cancellationToken); // held for this customer
        var items = new List<PublicAssistantLinkLine>();
        foreach (var line in link.Lines)
        {
            var variant = await catalog.GetPublishedVariantAsync(line.VariantId, cancellationToken);
            if (variant is null) continue;
            var product = await dbContext.Products.AsNoTracking().Where(p => p.Id == variant.ProductId).Select(p => new { p.Slug }).SingleAsync(cancellationToken);
            var visible = await dbContext.StoreProductPublications.AnyAsync(p => p.StoreId == store.StoreId && p.ProductId == variant.ProductId && p.Visibility == StoreProductVisibility.Visible, cancellationToken);
            if (!visible) continue;
            var available = (await inventory.GetAvailableQuantityAsync(line.VariantId, cancellationToken) ?? 0) + held.GetValueOrDefault(line.VariantId);
            items.Add(new PublicAssistantLinkLine(variant.ProductId, product.Slug, variant.ProductTitle, variant.VariantId, variant.VariantName, line.Quantity, variant.UnitPriceNpr, available >= line.Quantity));
        }

        return items.Count == 0
            ? Result<PublicAssistantLink>.NotFound("The items in this link are no longer available.")
            : Result<PublicAssistantLink>.Success(new PublicAssistantLink(link.ExpiresAt, items));
    }

    public async Task<string?> BeginCheckoutAsync(string token, string storeId, IReadOnlyList<string> variantIds, CancellationToken cancellationToken = default)
    {
        tenantContext.RequireCurrent();
        var hash = AssistantCheckoutLink.HashToken(token);
        var link = await dbContext.AssistantCheckoutLinks.SingleOrDefaultAsync(l => l.TokenHash == hash && l.StoreId == storeId, cancellationToken);
        if (link is null || !link.IsLive(timeProvider.UtcNow)) return null;

        await checkoutInventory.ReleaseConversationHoldsAsync(link.ConversationId, variantIds, cancellationToken); // caller's transaction
        return link.Id;
    }

    public async Task AttachCheckoutSessionAsync(string linkId, string checkoutSessionId, CancellationToken cancellationToken = default)
    {
        var link = await dbContext.AssistantCheckoutLinks.SingleOrDefaultAsync(l => l.Id == linkId, cancellationToken);
        link?.AttachCheckoutSession(checkoutSessionId);
    }

    public async Task OnOrderCreatedAsync(string checkoutSessionId, string orderId, string? customerId, CancellationToken cancellationToken = default)
    {
        tenantContext.RequireCurrent();
        var link = await dbContext.AssistantCheckoutLinks.SingleOrDefaultAsync(l => l.CheckoutSessionId == checkoutSessionId && l.State == AssistantCheckoutLinkState.Active, cancellationToken);
        if (link is null || !link.MarkUsed(orderId, timeProvider.UtcNow)) return;

        var linked = false;
        if (customerId is not null)
        {
            var identity = await dbContext.CustomerChannelIdentities.SingleOrDefaultAsync(i => i.Id == link.CustomerChannelIdentityId, cancellationToken);
            linked = identity?.LinkCustomer(customerId) == true;
        }

        await auditEvents.AppendAsync(new AuditEventWrite("assistant.checkout_link.used", "assistant-checkout-link", link.Id,
            Metadata: JsonSerializer.Serialize(new { orderId, conversationId = link.ConversationId, customerLinked = linked }),
            ActorKind: CommerceActorKind.CommerceSystem), cancellationToken);
    }

    private string Url(string storeSlug, string token)
    {
        var options = linkOptions.Value;
        return options.EnableDevelopmentSlugRoutes || string.IsNullOrWhiteSpace(options.PlatformBaseDomain)
            ? $"{options.StorefrontWebOrigin.TrimEnd('/')}/store/{storeSlug}/link/{token}"
            : $"https://{storeSlug}.{options.PlatformBaseDomain.Trim().ToLowerInvariant()}/link/{token}";
    }
}

/// <summary>Units held by an assistant chat (by conversation or by one of its live checkout links), per variant.</summary>
public sealed class ConversationHoldAllowance(AppDbContext dbContext, ITimeProvider timeProvider) : IConversationHoldAllowance
{
    public async Task<IReadOnlyDictionary<string, int>> HeldAsync(string storeId, string? conversationId, string? assistantLinkToken, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.UtcNow;
        if (conversationId is null && !string.IsNullOrWhiteSpace(assistantLinkToken))
        {
            var hash = AssistantCheckoutLink.HashToken(assistantLinkToken);
            conversationId = await dbContext.AssistantCheckoutLinks.AsNoTracking()
                .Where(l => l.TokenHash == hash && l.StoreId == storeId && l.State == AssistantCheckoutLinkState.Active && l.ExpiresAt > now)
                .Select(l => l.ConversationId).SingleOrDefaultAsync(cancellationToken);
        }

        if (conversationId is null) return new Dictionary<string, int>();
        return await dbContext.InventoryReservations.AsNoTracking()
            .Where(r => r.Source == Kreyora.Domain.Inventory.InventoryReservationSource.Conversation && r.ReferenceId == conversationId &&
                r.State == Kreyora.Domain.Inventory.InventoryReservationState.Active && r.ExpiresAt > now)
            .GroupBy(r => r.VariantId).Select(g => new { g.Key, Quantity = g.Sum(r => r.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Quantity, cancellationToken);
    }
}
