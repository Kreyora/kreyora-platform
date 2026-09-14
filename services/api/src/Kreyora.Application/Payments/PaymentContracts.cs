using Kreyora.Application.Models;
using Kreyora.Domain.Orders;
using Kreyora.Domain.Payments;

namespace Kreyora.Application.Payments;

public interface IPaymentService
{
    Task<Result<IReadOnlyList<PaymentAttemptItem>>> GetPaymentAttemptsAsync(string orderId, CancellationToken cancellationToken = default);
    Task<Result<PaymentProofItem>> InitiateProofUploadAsync(InitiatePaymentProofUploadRequest request, CancellationToken cancellationToken = default);
    Task<Result<PaymentProofItem>> CompleteProofUploadAsync(string paymentProofId, Stream content, CancellationToken cancellationToken = default);
    Task<Result<PaymentProofReadContent>> GetProofContentAsync(string proofId, CancellationToken cancellationToken = default);
}

public interface IStorePaymentConfigurationService
{
    Task<Result<StorePaymentConfigurationItem>> GetConfigurationAsync(string storeId, CancellationToken cancellationToken = default);
    Task<Result<StorePaymentConfigurationItem>> UpdateConfigurationAsync(UpdateStorePaymentConfigurationRequest request, CancellationToken cancellationToken = default);
}

public sealed record PaymentAttemptItem(
    string Id,
    string OrderId,
    OrderPaymentMethod Method,
    PaymentAttemptStatus Status,
    decimal AmountNpr,
    string Currency,
    string InternalReference,
    string? ProviderReference,
    DateTimeOffset? VerifiedAt,
    string? VerifiedByUserId,
    DateTimeOffset? RejectedAt,
    string? RejectedByUserId,
    string? RejectionReason,
    DateTimeOffset? CollectedAt,
    string? CollectedByUserId,
    IReadOnlyList<PaymentProofItem> Proofs);

public sealed record PaymentProofItem(
    string Id,
    string PaymentAttemptId,
    string ContentType,
    long ByteSize,
    PaymentProofStatus Status,
    string? CustomerNote,
    DateTimeOffset UploadExpiresAt,
    DateTimeOffset? ReadyAt);

public sealed record InitiatePaymentProofUploadRequest(
    string OrderId,
    string PaymentAttemptId,
    string ContentType,
    long ByteSize,
    string? CustomerNote = null);

public sealed record PaymentProofReadContent(Stream Content, string ContentType, long ByteSize);

public sealed record StorePaymentConfigurationItem(
    string Id,
    string StoreId,
    bool CodEnabled,
    bool MerchantQrEnabled,
    string? MerchantQrInstructions,
    string? MerchantQrMediaAssetId);

public sealed record UpdateStorePaymentConfigurationRequest(
    string StoreId,
    bool CodEnabled,
    bool MerchantQrEnabled,
    string? MerchantQrInstructions = null,
    string? MerchantQrMediaAssetId = null);

