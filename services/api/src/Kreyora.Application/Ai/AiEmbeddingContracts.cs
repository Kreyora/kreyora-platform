namespace Kreyora.Application.Ai;

/// <summary>Why a text is embedded; providers that support task types use it to tune vectors.</summary>
public enum AiEmbeddingPurpose
{
    Document,
    Query
}

/// <summary>
/// Provider-neutral embedding boundary (M09-S03, ADR-019). Obeys the same kill switch as chat: while AI is disabled
/// nothing is sent to a provider and callers fall back to lexical retrieval. Failures are values, never exceptions.
/// </summary>
public interface IAiEmbeddingClient
{
    Task<AiEmbeddingResult> EmbedAsync(IReadOnlyList<string> texts, AiEmbeddingPurpose purpose, CancellationToken cancellationToken = default);
}

public sealed record AiEmbeddingResult
{
    public bool IsSuccess { get; private init; }

    /// <summary>One vector per input text, in input order.</summary>
    public IReadOnlyList<float[]> Vectors { get; private init; } = [];

    /// <summary>Model identity stored with each vector so a model change triggers re-indexing.</summary>
    public string? Model { get; private init; }

    public int Dimensions { get; private init; }

    public AiFailureKind? Failure { get; private init; }

    public string? FailureMessage { get; private init; }

    public static AiEmbeddingResult Success(IReadOnlyList<float[]> vectors, string model, int dimensions) =>
        new() { IsSuccess = true, Vectors = vectors, Model = model, Dimensions = dimensions };

    public static AiEmbeddingResult Failed(AiFailureKind failure, string message) =>
        new() { IsSuccess = false, Failure = failure, FailureMessage = message };
}
