using Kreyora.Domain.Orders;
using Kreyora.Domain.Payments;

namespace Kreyora.UnitTests.Domain;

public class PaymentAttemptTests
{
    private static DateTimeOffset Now => DateTimeOffset.UtcNow;
    private const string TenantId = "01J00000000000000000000001";
    private const string OrderId = "01J00000000000000000000002";
    private const string UserId = "01J00000000000000000000003";

    [Fact]
    public void Create_CodAttempt_InitializesWithPendingStatus()
    {
        var attempt = PaymentAttempt.Create(TenantId, OrderId, OrderPaymentMethod.CashOnDelivery, 1500m);

        Assert.Equal(TenantId, attempt.TenantId);
        Assert.Equal(OrderId, attempt.OrderId);
        Assert.Equal(OrderPaymentMethod.CashOnDelivery, attempt.Method);
        Assert.Equal(PaymentAttemptStatus.Pending, attempt.Status);
        Assert.Equal(1500m, attempt.AmountNpr);
        Assert.Equal("NPR", attempt.Currency);
        Assert.StartsWith("PAY-", attempt.InternalReference);
        Assert.Null(attempt.ProviderReference);
    }

    [Fact]
    public void Create_MerchantQrAttempt_InitializesWithAwaitingProofStatus()
    {
        var attempt = PaymentAttempt.Create(TenantId, OrderId, OrderPaymentMethod.MerchantQr, 2500m);

        Assert.Equal(OrderPaymentMethod.MerchantQr, attempt.Method);
        Assert.Equal(PaymentAttemptStatus.AwaitingProof, attempt.Status);
        Assert.Equal(2500m, attempt.AmountNpr);
    }

    [Fact]
    public void Create_ThrowsOnNegativeAmount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PaymentAttempt.Create(TenantId, OrderId, OrderPaymentMethod.CashOnDelivery, -100m));
    }

    [Fact]
    public void AddProof_MerchantQrAttempt_TransitionsToProofSubmitted()
    {
        var attempt = PaymentAttempt.Create(TenantId, OrderId, OrderPaymentMethod.MerchantQr, 2000m);
        var proof = PaymentProof.CreatePending(TenantId, attempt.Id, "key", "image/png", 1024, Now.AddMinutes(15));

        attempt.AddProof(proof, Now);

        Assert.Single(attempt.Proofs);
        Assert.Equal(PaymentAttemptStatus.ProofSubmitted, attempt.Status);
    }

    [Fact]
    public void AddProof_ThrowsIfProofBelongsToDifferentAttemptOrTenant()
    {
        var attempt = PaymentAttempt.Create(TenantId, OrderId, OrderPaymentMethod.MerchantQr, 2000m);
        var mismatchProof = PaymentProof.CreatePending(TenantId, "01J00000000000000000000999", "key", "image/png", 1024, Now.AddMinutes(15));

        Assert.Throws<InvalidOperationException>(() => attempt.AddProof(mismatchProof, Now));
    }

    [Fact]
    public void Verify_MerchantQrAttempt_TransitionsToVerified()
    {
        var attempt = PaymentAttempt.Create(TenantId, OrderId, OrderPaymentMethod.MerchantQr, 2000m);

        attempt.Verify(UserId, Now, "ESEWA-TXN-12345");

        Assert.Equal(PaymentAttemptStatus.Verified, attempt.Status);
        Assert.Equal(UserId, attempt.VerifiedByUserId);
        Assert.NotNull(attempt.VerifiedAt);
        Assert.Equal("ESEWA-TXN-12345", attempt.ProviderReference);
    }

    [Fact]
    public void Verify_ThrowsIfAlreadyVerifiedOrRejected()
    {
        var attempt = PaymentAttempt.Create(TenantId, OrderId, OrderPaymentMethod.MerchantQr, 2000m);
        attempt.Verify(UserId, Now);

        Assert.Throws<InvalidOperationException>(() => attempt.Verify(UserId, Now));
        Assert.Throws<InvalidOperationException>(() => attempt.Reject(UserId, "Invalid slip", Now));
    }

    [Fact]
    public void Verify_ThrowsForCodAttempt()
    {
        var attempt = PaymentAttempt.Create(TenantId, OrderId, OrderPaymentMethod.CashOnDelivery, 1000m);

        Assert.Throws<InvalidOperationException>(() => attempt.Verify(UserId, Now));
    }

    [Fact]
    public void Reject_MerchantQrAttempt_TransitionsToRejected()
    {
        var attempt = PaymentAttempt.Create(TenantId, OrderId, OrderPaymentMethod.MerchantQr, 2000m);

        attempt.Reject(UserId, "Screenshot amount does not match order total.", Now);

        Assert.Equal(PaymentAttemptStatus.Rejected, attempt.Status);
        Assert.Equal(UserId, attempt.RejectedByUserId);
        Assert.NotNull(attempt.RejectedAt);
        Assert.Equal("Screenshot amount does not match order total.", attempt.RejectionReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")] // < 3 chars
    public void Reject_ThrowsOnInvalidReason(string invalidReason)
    {
        var attempt = PaymentAttempt.Create(TenantId, OrderId, OrderPaymentMethod.MerchantQr, 2000m);

        Assert.ThrowsAny<ArgumentException>(() => attempt.Reject(UserId, invalidReason, Now));
    }

    [Fact]
    public void MarkCollected_CodAttempt_TransitionsToCollected()
    {
        var attempt = PaymentAttempt.Create(TenantId, OrderId, OrderPaymentMethod.CashOnDelivery, 1500m);

        attempt.MarkCollected(UserId, Now, "RECEIPT-888");

        Assert.Equal(PaymentAttemptStatus.Collected, attempt.Status);
        Assert.Equal(UserId, attempt.CollectedByUserId);
        Assert.NotNull(attempt.CollectedAt);
        Assert.Equal("RECEIPT-888", attempt.ProviderReference);
    }

    [Fact]
    public void MarkCollected_ThrowsForMerchantQrAttempt()
    {
        var attempt = PaymentAttempt.Create(TenantId, OrderId, OrderPaymentMethod.MerchantQr, 1500m);

        Assert.Throws<InvalidOperationException>(() => attempt.MarkCollected(UserId, Now));
    }

    [Fact]
    public void MarkCollected_ThrowsIfAlreadyCollected()
    {
        var attempt = PaymentAttempt.Create(TenantId, OrderId, OrderPaymentMethod.CashOnDelivery, 1500m);
        attempt.MarkCollected(UserId, Now);

        Assert.Throws<InvalidOperationException>(() => attempt.MarkCollected(UserId, Now));
    }
}

