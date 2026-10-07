using System.Security.Cryptography;
using System.Text;
using Kreyora.Domain.Common;

namespace Kreyora.Domain.Assistant;

public enum AssistantCheckoutLinkState
{
    Active = 1,
    Used = 2
}

/// <summary>One cart line in a checkout link. Prices are never stored: the storefront reprices at checkout.</summary>
public sealed record CheckoutLinkLine(string VariantId, int Quantity);

/// <summary>
/// A storefront link the assistant sends so the customer can check out with the cart already filled (M09-S05 Q2).
/// Only the token's SHA-256 is stored. The order still goes through the normal checkout; when it does, the link is
/// marked used and the chat identity is linked to the customer.
/// </summary>
public sealed class AssistantCheckoutLink : BaseEntity, ITenantOwned
{
    public const int MaxLines = 10;
    public const int MaxQuantity = 5;

    private AssistantCheckoutLink() { }

    public string TenantId { get; private set; } = string.Empty;
    public string StoreId { get; private set; } = string.Empty;
    public string ConversationId { get; private set; } = string.Empty;
    public string CustomerChannelIdentityId { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public string LinesFingerprint { get; private set; } = string.Empty;
    public List<CheckoutLinkLine> Lines { get; private set; } = [];
    public DateTimeOffset ExpiresAt { get; private set; }
    public AssistantCheckoutLinkState State { get; private set; }
    public string? CheckoutSessionId { get; private set; }
    public string? OrderId { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }

    /// <summary>Creates a link and returns the raw token, which exists only in the returned URL.</summary>
    public static (AssistantCheckoutLink Link, string Token) Create(
        string tenantId, string storeId, string conversationId, string customerChannelIdentityId, IReadOnlyList<CheckoutLinkLine> lines, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count is 0 or > MaxLines) throw new ArgumentException($"A checkout link needs 1-{MaxLines} lines.", nameof(lines));
        if (lines.Any(l => l.Quantity is < 1 or > MaxQuantity)) throw new ArgumentOutOfRangeException(nameof(lines), $"Quantities must be between 1 and {MaxQuantity}.");
        if (lines.GroupBy(l => l.VariantId, StringComparer.Ordinal).Any(g => g.Count() > 1)) throw new ArgumentException("Each item may appear once.", nameof(lines));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(expiresAt, now);

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var ordered = lines.OrderBy(l => l.VariantId, StringComparer.Ordinal).ToList();
        var link = new AssistantCheckoutLink
        {
            TenantId = Require(tenantId), StoreId = Require(storeId), ConversationId = Require(conversationId),
            CustomerChannelIdentityId = Require(customerChannelIdentityId), TokenHash = HashToken(token), Lines = ordered,
            LinesFingerprint = Fingerprint(ordered), ExpiresAt = expiresAt, State = AssistantCheckoutLinkState.Active
        };
        return (link, token);
    }

    public bool IsLive(DateTimeOffset now) => State == AssistantCheckoutLinkState.Active && ExpiresAt > now;

    public void AttachCheckoutSession(string checkoutSessionId) => CheckoutSessionId = Require(checkoutSessionId);

    /// <summary>Marks the link used by an order; returns false if it was already used (one order per link).</summary>
    public bool MarkUsed(string orderId, DateTimeOffset now)
    {
        if (State == AssistantCheckoutLinkState.Used) return false;
        State = AssistantCheckoutLinkState.Used;
        OrderId = Require(orderId);
        UsedAt = now;
        return true;
    }

    public static string HashToken(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));

    public static string Fingerprint(IEnumerable<CheckoutLinkLine> lines) =>
        string.Join(';', lines.OrderBy(l => l.VariantId, StringComparer.Ordinal).Select(l => $"{l.VariantId}x{l.Quantity}"));

    private static string Require(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Length > 26 ? throw new ArgumentException("A valid identifier is required.", nameof(value)) : value;
}

public enum AssistantActionStatus
{
    AwaitingConfirmation = 1,
    Completed = 2,
    Superseded = 3
}

/// <summary>
/// A write-tool action of one conversation (M09-S05): either a proposal awaiting the customer's confirmation (Q4), or a
/// completed call whose result replays for duplicate calls (idempotency). Stores a fingerprint of the arguments and the
/// result shown to the model, never customer text.
/// </summary>
public sealed class AssistantAction : BaseEntity, ITenantOwned
{
    public const int ResultMaxLength = 8000;

    private AssistantAction() { }

    public string TenantId { get; private set; } = string.Empty;
    public string ConversationId { get; private set; } = string.Empty;
    public string Tool { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string ArgumentsFingerprint { get; private set; } = string.Empty;
    public AssistantActionStatus Status { get; private set; }
    public string? ResultJson { get; private set; }

    /// <summary>Server-only reference kept for a later tool (e.g. the signed quote behind a <c>quoteId</c>); never shown to the model.</summary>
    public string? InternalReference { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public static AssistantAction Propose(string tenantId, string conversationId, string tool, string idempotencyKey, string argumentsFingerprint, DateTimeOffset expiresAt) => new()
    {
        TenantId = tenantId, ConversationId = conversationId, Tool = tool, IdempotencyKey = idempotencyKey,
        ArgumentsFingerprint = argumentsFingerprint, Status = AssistantActionStatus.AwaitingConfirmation, ExpiresAt = expiresAt
    };

    public static AssistantAction Completed(string id, string tenantId, string conversationId, string tool, string idempotencyKey, string argumentsFingerprint, string resultJson,
        string? internalReference, DateTimeOffset? expiresAt, DateTimeOffset now) => new()
    {
        Id = id, InternalReference = internalReference, ExpiresAt = expiresAt,
        TenantId = tenantId, ConversationId = conversationId, Tool = tool, IdempotencyKey = idempotencyKey,
        ArgumentsFingerprint = argumentsFingerprint, Status = AssistantActionStatus.Completed,
        ResultJson = resultJson.Length > ResultMaxLength ? resultJson[..ResultMaxLength] : resultJson, CompletedAt = now
    };

    /// <summary>A proposal can be confirmed once, before it expires, by the same conversation with the same arguments.</summary>
    public bool CanConfirm(string conversationId, string tool, string argumentsFingerprint, DateTimeOffset now) =>
        Status == AssistantActionStatus.AwaitingConfirmation && ExpiresAt > now &&
        ConversationId == conversationId && Tool == tool && ArgumentsFingerprint == argumentsFingerprint;

    public void Confirm(DateTimeOffset now)
    {
        Status = AssistantActionStatus.Superseded;
        CompletedAt = now;
    }
}
