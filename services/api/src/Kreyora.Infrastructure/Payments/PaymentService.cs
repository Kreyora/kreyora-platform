using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Catalog;
using Kreyora.Application.Models;
using Kreyora.Application.Payments;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Common;
using Kreyora.Domain.Payments;
using Kreyora.Infrastructure.Media;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Payments;

public sealed class PaymentService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    ITenantPermissionAuthorizer permissionAuthorizer,
    ITenantKeyBuilder tenantKeys,
    IPrivateObjectStorage storage,
    IAuditEventService auditEvents,
    Domain.Abstractions.ITimeProvider timeProvider,
    IOptions<MediaStorageOptions> options) : IPaymentService
{
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.Ordinal)
    {
        ["image/jpeg"] = "jpg",
        ["image/png"] = "png",
        ["image/webp"] = "webp",
        ["application/pdf"] = "pdf"
    };

    public async Task<Result<IReadOnlyList<PaymentAttemptItem>>> GetPaymentAttemptsAsync(string orderId, CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.PaymentsRead);
        _ = tenantContext.RequireCurrent();

        var normalizedOrderId = Require(orderId, nameof(orderId), 26);
        var orderExists = await dbContext.Orders.AnyAsync(o => o.Id == normalizedOrderId, cancellationToken);
        if (!orderExists)
        {
            return Result<IReadOnlyList<PaymentAttemptItem>>.NotFound("Order not found.");
        }

        var attempts = await dbContext.PaymentAttempts
            .Include(a => a.Proofs)
            .Where(a => a.OrderId == normalizedOrderId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<PaymentAttemptItem>>.Success(attempts.Select(Map).ToList());
    }

    public async Task<Result<PaymentProofItem>> InitiateProofUploadAsync(InitiatePaymentProofUploadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.OrdersWrite);
        var context = tenantContext.RequireCurrent();

        try
        {
            var normalizedOrderId = Require(request.OrderId, nameof(request.OrderId), 26);
            var normalizedAttemptId = Require(request.PaymentAttemptId, nameof(request.PaymentAttemptId), 26);
            var contentType = NormalizeContentType(request.ContentType);
            ValidateSize(request.ByteSize);

            var attempt = await dbContext.PaymentAttempts
                .SingleOrDefaultAsync(a => a.Id == normalizedAttemptId && a.OrderId == normalizedOrderId, cancellationToken);

            if (attempt is null)
            {
                return Result<PaymentProofItem>.NotFound("Payment attempt not found.");
            }

            if (attempt.Method != Domain.Orders.OrderPaymentMethod.MerchantQr)
            {
                return Result<PaymentProofItem>.ValidationError("Payment proof can only be uploaded for merchant QR payment attempts.");
            }

            if (attempt.Status is PaymentAttemptStatus.Verified or PaymentAttemptStatus.Collected or PaymentAttemptStatus.Expired)
            {
                return Result<PaymentProofItem>.ValidationError($"Cannot upload payment proof for attempt in {attempt.Status} status.");
            }

            var proofId = IdGenerator.NewId();
            var objectKey = tenantKeys.BuildStorageObjectKey("payment-proofs", proofId, $"proof.{Extensions[contentType]}");
            var proof = PaymentProof.CreatePending(
                context.TenantId,
                attempt.Id,
                objectKey,
                contentType,
                request.ByteSize,
                timeProvider.UtcNow.Add(options.Value.UploadLifetime),
                request.CustomerNote);

            dbContext.PaymentProofs.Add(proof);
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditEvents.AppendAsync(new AuditEventWrite(
                Action: "payment.proof.upload.initiated",
                TargetType: "payment-proof",
                TargetId: proof.Id,
                Metadata: $"{{\"paymentAttemptId\":\"{attempt.Id}\",\"contentType\":\"{proof.ContentType}\",\"byteSize\":{proof.ByteSize}}}"), cancellationToken);

            return Result<PaymentProofItem>.Success(MapProof(proof));
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return Result<PaymentProofItem>.ValidationError(exception.Message);
        }
    }

    public async Task<Result<PaymentProofItem>> CompleteProofUploadAsync(string paymentProofId, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        permissionAuthorizer.Demand(TenantPermissions.OrdersWrite);
        _ = tenantContext.RequireCurrent();

        var normalizedProofId = Require(paymentProofId, nameof(paymentProofId), 26);
        var proof = await dbContext.PaymentProofs
            .SingleOrDefaultAsync(p => p.Id == normalizedProofId, cancellationToken);

        if (proof is null)
        {
            return Result<PaymentProofItem>.NotFound("Payment proof not found.");
        }

        var attempt = await dbContext.PaymentAttempts
            .SingleOrDefaultAsync(a => a.Id == proof.PaymentAttemptId, cancellationToken);

        if (attempt is null)
        {
            return Result<PaymentProofItem>.NotFound("Associated payment attempt not found.");
        }

        try
        {
            var now = timeProvider.UtcNow;
            if (now > proof.UploadExpiresAt)
            {
                return Result<PaymentProofItem>.Conflict("The payment proof upload has expired.");
            }

            var bytes = await ReadExactlyAsync(content, proof.ByteSize, cancellationToken);
            if (!HasExpectedSignature(proof.ContentType, bytes))
            {
                return Result<PaymentProofItem>.ValidationError("Payment proof content does not match its declared type.");
            }

            await using var upload = new MemoryStream(bytes, writable: false);
            await storage.PutAsync(new StorageObjectWrite(proof.ObjectKey, proof.ContentType, proof.ByteSize, upload), cancellationToken);

            proof.Complete(now);
            attempt.AddProof(proof, now);

            await dbContext.SaveChangesAsync(cancellationToken);

            await auditEvents.AppendAsync(new AuditEventWrite(
                Action: "payment.proof.uploaded",
                TargetType: "payment-proof",
                TargetId: proof.Id,
                Metadata: $"{{\"paymentAttemptId\":\"{attempt.Id}\",\"contentType\":\"{proof.ContentType}\",\"byteSize\":{proof.ByteSize}}}"), cancellationToken);

            return Result<PaymentProofItem>.Success(MapProof(proof));
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return Result<PaymentProofItem>.ValidationError(exception.Message);
        }
    }

    public async Task<Result<PaymentProofReadContent>> GetProofContentAsync(string proofId, CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.PaymentsRead);
        _ = tenantContext.RequireCurrent();

        var normalizedProofId = Require(proofId, nameof(proofId), 26);
        var proof = await dbContext.PaymentProofs
            .SingleOrDefaultAsync(p => p.Id == normalizedProofId, cancellationToken);

        if (proof is null)
        {
            return Result<PaymentProofReadContent>.NotFound("Payment proof not found.");
        }

        if (proof.Status != PaymentProofStatus.Ready)
        {
            return Result<PaymentProofReadContent>.ValidationError($"Payment proof is not ready for viewing ({proof.Status}).");
        }

        var stream = await storage.OpenReadAsync(proof.ObjectKey, cancellationToken);
        if (stream is null)
        {
            return Result<PaymentProofReadContent>.NotFound("Payment proof storage object not found.");
        }

        return Result<PaymentProofReadContent>.Success(new PaymentProofReadContent(stream, proof.ContentType, proof.ByteSize));
    }

    private static PaymentAttemptItem Map(PaymentAttempt attempt) => new(
        attempt.Id,
        attempt.OrderId,
        attempt.Method,
        attempt.Status,
        attempt.AmountNpr,
        attempt.Currency,
        attempt.InternalReference,
        attempt.ProviderReference,
        attempt.VerifiedAt,
        attempt.VerifiedByUserId,
        attempt.RejectedAt,
        attempt.RejectedByUserId,
        attempt.RejectionReason,
        attempt.CollectedAt,
        attempt.CollectedByUserId,
        attempt.Proofs.Select(MapProof).ToList());

    private static PaymentProofItem MapProof(PaymentProof proof) => new(
        proof.Id,
        proof.PaymentAttemptId,
        proof.ContentType,
        proof.ByteSize,
        proof.Status,
        proof.CustomerNote,
        proof.UploadExpiresAt,
        proof.ReadyAt);

    private static string NormalizeContentType(string contentType)
    {
        var normalized = string.IsNullOrWhiteSpace(contentType)
            ? throw new ArgumentException("Content type is required.", nameof(contentType))
            : contentType.Trim().ToLowerInvariant();

        return Extensions.ContainsKey(normalized)
            ? normalized
            : throw new ArgumentException("Only JPEG, PNG, WebP images, and PDF documents are allowed for payment proof.", nameof(contentType));
    }

    private void ValidateSize(long byteSize)
    {
        if (byteSize <= 0 || byteSize > options.Value.MaxUploadBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(byteSize), "Payment proof size exceeds the allowed limit.");
        }
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream content, long expectedSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(expectedSize, (long)int.MaxValue);
        await using var buffer = new MemoryStream((int)expectedSize);
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length != expectedSize)
        {
            throw new ArgumentException("Upload size does not match declared size.");
        }
        return buffer.ToArray();
    }

    private static bool HasExpectedSignature(string contentType, ReadOnlySpan<byte> bytes) => contentType switch
    {
        "image/jpeg" => bytes.Length >= 3 && bytes[..3].SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF }),
        "image/png" => bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        "image/webp" => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8),
        "application/pdf" => bytes.Length >= 5 && bytes[..5].SequenceEqual("%PDF-"u8),
        _ => false
    };

    private static string Require(string value, string parameterName, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
        return normalized.Length > maximumLength ? throw new ArgumentOutOfRangeException(parameterName) : normalized;
    }
}

