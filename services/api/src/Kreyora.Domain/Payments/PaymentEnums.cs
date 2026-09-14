namespace Kreyora.Domain.Payments;

public enum PaymentAttemptStatus
{
    Pending,
    AwaitingProof,
    ProofSubmitted,
    Verified,
    Rejected,
    Collected,
    Expired
}

public enum PaymentProofStatus
{
    UploadPending,
    Ready,
    DeletionPending,
    Deleted
}

