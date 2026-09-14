using Kreyora.Domain.Common;
using Kreyora.Domain.Orders;

namespace Kreyora.Domain.Payments;

public sealed class PaymentAttempt : BaseEntity, ITenantOwned
{
    public const int ReferenceMaxLength = 64;
    public const int ProviderReferenceMaxLength = 128;
    public const int ReasonMaxLength = 500;
    public const int ReasonMinLength = 3;

    private PaymentAttempt() { }

    public string TenantId { get; private set; } = string.Empty;
    public string OrderId { get; private set; } = string.Empty;
    public OrderPaymentMethod Method { get; private set; }
    public PaymentAttemptStatus Status { get; private set; }
    public decimal AmountNpr { get; private set; }
    public string Currency { get; private set; } = "NPR";
    public string InternalReference { get; private set; } = string.Empty;
    public string? ProviderReference { get; private set; }
    public DateTimeOffset? VerifiedAt { get; private set; }
    public string? VerifiedByUserId { get; private set; }
    public DateTimeOffset? RejectedAt { get; private set; }
    public string? RejectedByUserId { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTimeOffset? CollectedAt { get; private set; }
    public string? CollectedByUserId { get; private set; }
    public List<PaymentProof> Proofs { get; private set; } = [];

    public static PaymentAttempt Create(
        string tenantId,
        string orderId,
        OrderPaymentMethod method,
        decimal amountNpr,
        string currency = "NPR",
        string? internalReference = null,
        string? providerReference = null)
    {
        if (!Enum.IsDefined(method)) throw new ArgumentOutOfRangeException(nameof(method));
        if (amountNpr < 0) throw new ArgumentOutOfRangeException(nameof(amountNpr), "Payment amount cannot be negative.");

        var attempt = new PaymentAttempt
        {
            TenantId = Require(tenantId, nameof(tenantId), 26),
            OrderId = Require(orderId, nameof(orderId), 26),
            Method = method,
            Status = method == OrderPaymentMethod.CashOnDelivery
                ? PaymentAttemptStatus.Pending
                : PaymentAttemptStatus.AwaitingProof,
            AmountNpr = amountNpr,
            Currency = Require(currency, nameof(currency), 3),
            ProviderReference = Optional(providerReference, ProviderReferenceMaxLength)
        };

        attempt.InternalReference = internalReference is not null
            ? Require(internalReference, nameof(internalReference), ReferenceMaxLength)
            : $"PAY-{attempt.Id}";

        return attempt;
    }

    public void AddProof(PaymentProof proof, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(proof);
        if (proof.TenantId != TenantId || proof.PaymentAttemptId != Id)
        {
            throw new InvalidOperationException("Payment proof must belong to this payment attempt.");
        }

        if (Status is PaymentAttemptStatus.Verified or PaymentAttemptStatus.Collected or PaymentAttemptStatus.Expired)
        {
            throw new InvalidOperationException($"Cannot add proof to a payment attempt in {Status} status.");
        }

        if (!Proofs.Any(p => p.Id == proof.Id))
        {
            Proofs.Add(proof);
        }
        if (Method == OrderPaymentMethod.MerchantQr && Status is PaymentAttemptStatus.Pending or PaymentAttemptStatus.AwaitingProof)
        {
            Status = PaymentAttemptStatus.ProofSubmitted;
        }

        ModifiedAt = now;
    }

    public void Verify(string userId, DateTimeOffset now, string? providerReference = null)
    {
        if (Method != OrderPaymentMethod.MerchantQr)
        {
            throw new InvalidOperationException("Only merchant QR payment attempts can be verified.");
        }

        if (Status == PaymentAttemptStatus.Verified)
        {
            throw new InvalidOperationException("Payment attempt has already been verified.");
        }

        if (Status is PaymentAttemptStatus.Rejected or PaymentAttemptStatus.Collected or PaymentAttemptStatus.Expired)
        {
            throw new InvalidOperationException($"Payment attempt in {Status} status cannot be verified.");
        }

        Status = PaymentAttemptStatus.Verified;
        VerifiedAt = now;
        VerifiedByUserId = Require(userId, nameof(userId), 26);
        if (!string.IsNullOrWhiteSpace(providerReference))
        {
            ProviderReference = Require(providerReference, nameof(providerReference), ProviderReferenceMaxLength);
        }

        ModifiedAt = now;
    }

    public void Reject(string userId, string reason, DateTimeOffset now)
    {
        if (Method != OrderPaymentMethod.MerchantQr)
        {
            throw new InvalidOperationException("Only merchant QR payment attempts can be rejected.");
        }

        if (Status == PaymentAttemptStatus.Verified)
        {
            throw new InvalidOperationException("A verified payment attempt cannot be rejected.");
        }

        if (Status == PaymentAttemptStatus.Rejected)
        {
            throw new InvalidOperationException("Payment attempt has already been rejected.");
        }

        if (Status is PaymentAttemptStatus.Collected or PaymentAttemptStatus.Expired)
        {
            throw new InvalidOperationException($"Payment attempt in {Status} status cannot be rejected.");
        }

        var trimmed = string.IsNullOrWhiteSpace(reason) ? throw new ArgumentException("A reason is required to reject payment.", nameof(reason)) : reason.Trim();
        if (trimmed.Length < ReasonMinLength || trimmed.Length > ReasonMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(reason), $"Rejection reason must be between {ReasonMinLength} and {ReasonMaxLength} characters.");
        }

        Status = PaymentAttemptStatus.Rejected;
        RejectedAt = now;
        RejectedByUserId = Require(userId, nameof(userId), 26);
        RejectionReason = trimmed;
        ModifiedAt = now;
    }

    public void MarkCollected(string userId, DateTimeOffset now, string? providerReference = null)
    {
        if (Method != OrderPaymentMethod.CashOnDelivery)
        {
            throw new InvalidOperationException("Only cash on delivery payment attempts can be marked collected.");
        }

        if (Status == PaymentAttemptStatus.Collected)
        {
            throw new InvalidOperationException("Payment attempt has already been collected.");
        }

        if (Status != PaymentAttemptStatus.Pending)
        {
            throw new InvalidOperationException($"COD payment attempt must be pending to be marked collected, but is {Status}.");
        }

        Status = PaymentAttemptStatus.Collected;
        CollectedAt = now;
        CollectedByUserId = Require(userId, nameof(userId), 26);
        if (!string.IsNullOrWhiteSpace(providerReference))
        {
            ProviderReference = Optional(providerReference, ProviderReferenceMaxLength);
        }

        ModifiedAt = now;
    }

    public void Expire(DateTimeOffset now)
    {
        if (Status is PaymentAttemptStatus.Verified or PaymentAttemptStatus.Collected)
        {
            throw new InvalidOperationException($"Cannot expire payment attempt in {Status} status.");
        }

        if (Status == PaymentAttemptStatus.Expired)
        {
            return;
        }

        Status = PaymentAttemptStatus.Expired;
        ModifiedAt = now;
    }

    private static string Require(string value, string parameterName, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
        return normalized.Length > maximumLength ? throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maximumLength} characters.") : normalized;
    }

    private static string? Optional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length > maximumLength ? throw new ArgumentOutOfRangeException(nameof(value), $"Value cannot exceed {maximumLength} characters.") : normalized;
    }
}
