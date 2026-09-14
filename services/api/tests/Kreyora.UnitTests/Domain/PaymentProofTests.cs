using Kreyora.Domain.Payments;

namespace Kreyora.UnitTests.Domain;

public class PaymentProofTests
{
    private static DateTimeOffset Now => DateTimeOffset.UtcNow;
    private const string TenantId = "01J00000000000000000000001";
    private const string AttemptId = "01J00000000000000000000002";

    [Fact]
    public void CreatePending_ValidatesInputsAndSetsInitialState()
    {
        var proof = PaymentProof.CreatePending(
            TenantId,
            AttemptId,
            "tenants/t1/payment-proofs/p1/proof.jpg",
            "image/jpeg",
            2048,
            Now.AddMinutes(30),
            "Paid via eSewa");

        Assert.Equal(TenantId, proof.TenantId);
        Assert.Equal(AttemptId, proof.PaymentAttemptId);
        Assert.Equal("image/jpeg", proof.ContentType);
        Assert.Equal(2048, proof.ByteSize);
        Assert.Equal(PaymentProofStatus.UploadPending, proof.Status);
        Assert.Equal("Paid via eSewa", proof.CustomerNote);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/zip")]
    [InlineData("image/gif")]
    public void CreatePending_ThrowsOnUnsupportedContentType(string unsupportedType)
    {
        Assert.Throws<ArgumentException>(() =>
            PaymentProof.CreatePending(TenantId, AttemptId, "key", unsupportedType, 1024, Now.AddMinutes(15)));
    }

    [Fact]
    public void CompleteAndDeletion_EnforcesLifecycle()
    {
        var proof = PaymentProof.CreatePending(TenantId, AttemptId, "key", "image/png", 1024, Now.AddMinutes(15));

        proof.Complete(Now);
        Assert.Equal(PaymentProofStatus.Ready, proof.Status);
        Assert.NotNull(proof.ReadyAt);

        proof.RequestDeletion(Now.AddMinutes(1));
        Assert.Equal(PaymentProofStatus.DeletionPending, proof.Status);

        proof.MarkDeleted(Now.AddMinutes(2));
        Assert.Equal(PaymentProofStatus.Deleted, proof.Status);
    }

    [Fact]
    public void Complete_ThrowsIfExpired()
    {
        var proof = PaymentProof.CreatePending(TenantId, AttemptId, "key", "image/png", 1024, Now.AddMinutes(15));

        Assert.Throws<InvalidOperationException>(() => proof.Complete(Now.AddMinutes(16)));
    }
}

