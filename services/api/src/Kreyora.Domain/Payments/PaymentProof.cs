using Kreyora.Domain.Common;

namespace Kreyora.Domain.Payments;

public sealed class PaymentProof : BaseEntity, ITenantOwned
{
    public const int ObjectKeyMaxLength = 512;
    public const int ContentTypeMaxLength = 100;
    public const int NoteMaxLength = 500;

    private PaymentProof() { }

    public string TenantId { get; private set; } = string.Empty;
    public string PaymentAttemptId { get; private set; } = string.Empty;
    public string ObjectKey { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long ByteSize { get; private set; }
    public PaymentProofStatus Status { get; private set; }
    public string? CustomerNote { get; private set; }
    public DateTimeOffset UploadExpiresAt { get; private set; }
    public DateTimeOffset? ReadyAt { get; private set; }
    public DateTimeOffset? DeletionRequestedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public static PaymentProof CreatePending(
        string tenantId,
        string paymentAttemptId,
        string objectKey,
        string contentType,
        long byteSize,
        DateTimeOffset uploadExpiresAt,
        string? customerNote = null)
    {
        if (byteSize <= 0) throw new ArgumentOutOfRangeException(nameof(byteSize), "Proof size must be greater than zero.");
        if (uploadExpiresAt <= DateTimeOffset.UtcNow) throw new ArgumentOutOfRangeException(nameof(uploadExpiresAt), "Proof upload expiry must be in the future.");

        var normalizedContentType = Require(contentType, nameof(contentType), ContentTypeMaxLength).ToLowerInvariant();
        if (!IsValidProofContentType(normalizedContentType))
        {
            throw new ArgumentException($"Content type '{contentType}' is not supported for payment proof.", nameof(contentType));
        }

        return new PaymentProof
        {
            TenantId = Require(tenantId, nameof(tenantId), 26),
            PaymentAttemptId = Require(paymentAttemptId, nameof(paymentAttemptId), 26),
            ObjectKey = Require(objectKey, nameof(objectKey), ObjectKeyMaxLength),
            ContentType = normalizedContentType,
            ByteSize = byteSize,
            Status = PaymentProofStatus.UploadPending,
            UploadExpiresAt = uploadExpiresAt,
            CustomerNote = Optional(customerNote, NoteMaxLength)
        };
    }

    public void Complete(DateTimeOffset now)
    {
        EnsureStatus(PaymentProofStatus.UploadPending);
        if (now > UploadExpiresAt) throw new InvalidOperationException("The payment proof upload has expired.");
        Status = PaymentProofStatus.Ready;
        ReadyAt = now;
        ModifiedAt = now;
    }

    public void RequestDeletion(DateTimeOffset now)
    {
        if (Status is PaymentProofStatus.Deleted or PaymentProofStatus.DeletionPending) return;
        Status = PaymentProofStatus.DeletionPending;
        DeletionRequestedAt = now;
        ModifiedAt = now;
    }

    public void MarkDeleted(DateTimeOffset now)
    {
        EnsureStatus(PaymentProofStatus.DeletionPending);
        Status = PaymentProofStatus.Deleted;
        DeletedAt = now;
        ModifiedAt = now;
    }

    private void EnsureStatus(PaymentProofStatus expected)
    {
        if (Status != expected) throw new InvalidOperationException($"Payment proof must be {expected} but is {Status}.");
    }

    private static bool IsValidProofContentType(string contentType) =>
        contentType is "image/jpeg" or "image/png" or "image/webp" or "application/pdf";

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

