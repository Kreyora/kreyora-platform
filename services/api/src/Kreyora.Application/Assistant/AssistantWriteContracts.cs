using Kreyora.Application.Models;
using Kreyora.Domain.Assistant;

namespace Kreyora.Application.Assistant;

// ---- Write-tool services (M09-S05, ADR-020). Tools stay thin; the rules live here and in the reused commands. ----

public sealed record AssistantCartLine(string VariantId, int Quantity);

public sealed record AssistantQuoteLine(string VariantId, string ProductTitle, string VariantName, int Quantity, decimal UnitPriceNpr, decimal LineSubtotalNpr);

public sealed record AssistantQuote(
    string? QuoteId,
    string Place,
    IReadOnlyList<AssistantQuoteLine> Lines,
    decimal MerchandiseSubtotalNpr,
    decimal DeliveryFeeNpr,
    decimal TotalNpr,
    string? EtaText,
    bool CashOnDelivery,
    DateTimeOffset ExpiresAt);

/// <summary>Outcome of a write service: a value, or a stable tool error code with optional data for the model.</summary>
public sealed record AssistantWriteOutcome<T>(T? Value, string? ErrorCode, string? Message, object? Data = null)
{
    public bool IsSuccess => ErrorCode is null;
}

public static class AssistantWriteOutcome
{
    public static AssistantWriteOutcome<T> Ok<T>(T value) => new(value, null, null);

    public static AssistantWriteOutcome<T> Fail<T>(string code, string message, object? data = null) => new(default, code, message, data);
}

/// <summary>QuoteCart: resolves the customer's place, then prices through the storefront quote service (stateless).</summary>
public interface IAssistantQuoteService
{
    Task<AssistantWriteOutcome<AssistantQuote>> QuoteAsync(AssistantToolContext context, string place, IReadOnlyList<AssistantCartLine> items, CancellationToken cancellationToken = default);
}

public sealed record AssistantHold(string ReservationId, string VariantId, int Quantity, DateTimeOffset ExpiresAt);

public sealed record AssistantHoldProposal(string ConfirmationId, IReadOnlyList<AssistantCartLine> Items, DateTimeOffset ExpiresAt);

public sealed record AssistantHoldResult(AssistantHoldProposal? Proposal, IReadOnlyList<AssistantHold> Holds, bool DryRun);

/// <summary>ReserveInventory / ReleaseReservation: bounded chat holds with two-phase confirmation (Q3, Q4).</summary>
public interface IAssistantHoldService
{
    Task<AssistantWriteOutcome<AssistantHoldResult>> ReserveAsync(AssistantToolContext context, IReadOnlyList<AssistantCartLine> items, string? confirmationId, CancellationToken cancellationToken = default);

    Task<AssistantWriteOutcome<IReadOnlyList<AssistantHold>>> ReleaseAsync(AssistantToolContext context, IReadOnlyList<string>? reservationIds, CancellationToken cancellationToken = default);
}

public sealed record AssistantCheckoutLinkCreated(string? Url, DateTimeOffset ExpiresAt, IReadOnlyList<AssistantCartLine> Items, bool Reused, bool DryRun);

public sealed record PublicAssistantLinkLine(string ProductId, string ProductSlug, string ProductTitle, string VariantId, string VariantName, int Quantity, decimal UnitPriceNpr, bool Available);

/// <summary>A checkout link as the storefront sees it: current prices and availability, no customer data.</summary>
public sealed record PublicAssistantLink(DateTimeOffset ExpiresAt, IReadOnlyList<PublicAssistantLinkLine> Items);

/// <summary>CreateCheckoutLink plus the public read used by the storefront landing page (Q2).</summary>
public interface IAssistantCheckoutLinkService
{
    Task<AssistantWriteOutcome<AssistantCheckoutLinkCreated>> CreateAsync(AssistantToolContext context, IReadOnlyList<AssistantCartLine> items, string? quoteId, CancellationToken cancellationToken = default);

    Task<Result<PublicAssistantLink>> GetPublicAsync(string token, CancellationToken cancellationToken = default);
}

/// <summary>
/// Hooks the normal checkout calls when a session comes from an assistant link: inside the session transaction the
/// chat's holds hand over to checkout; inside the order transaction the link is marked used and the chat identity is
/// linked to the customer. Invalid, expired or foreign tokens are ignored (the customer can still buy).
/// </summary>
public interface IAssistantCheckoutLinkHandover
{
    /// <summary>
    /// Releases the link's chat holds on these variants inside the caller's transaction (before revalidation, so the
    /// same units can be reserved for checkout). Returns the link ID, or null when the token doesn't apply.
    /// </summary>
    Task<string?> BeginCheckoutAsync(string token, string storeId, IReadOnlyList<string> variantIds, CancellationToken cancellationToken = default);

    /// <summary>Records which checkout session came from the link (tracked; saved with the session).</summary>
    Task AttachCheckoutSessionAsync(string linkId, string checkoutSessionId, CancellationToken cancellationToken = default);

    Task OnOrderCreatedAsync(string checkoutSessionId, string orderId, string? customerId, CancellationToken cancellationToken = default);
}

/// <summary>Escalation categories the assistant may give (Q7): the fixed S02 situations plus assistant-side reasons.</summary>
public static class AssistantEscalationCategories
{
    public static readonly IReadOnlyList<string> All =
    [
        .. AssistantPolicy.FixedEscalationCategories,
        "keyword_match", "low_confidence", "tool_unavailable", "outside_scope", "other"
    ];
}
