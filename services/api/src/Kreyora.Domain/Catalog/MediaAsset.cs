using Kreyora.Domain.Common;
using Kreyora.Domain.Abstractions;

namespace Kreyora.Domain.Catalog;

public sealed class MediaAsset : BaseEntity, ITenantOwned
{
    public const int ObjectKeyMaxLength = 512;
    public const int ContentTypeMaxLength = 100;
    public const int AltTextMaxLength = 300;

    private MediaAsset()
    {
    }

    public string TenantId { get; private set; } = string.Empty;
    public string ObjectKey { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long ByteSize { get; private set; }
    public MediaAssetState State { get; private set; }
    public string? ProductId { get; private set; }
    public int? SortOrder { get; private set; }
    public string? AltText { get; private set; }
    public DateTimeOffset UploadExpiresAt { get; private set; }
    public DateTimeOffset? ReadyAt { get; private set; }
    public DateTimeOffset? DeletionRequestedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public static MediaAsset CreatePending(string tenantId, string objectKey, string contentType, long byteSize, DateTimeOffset uploadExpiresAt, string? id = null)
    {
        if (byteSize <= 0) throw new ArgumentOutOfRangeException(nameof(byteSize), "Media size must be greater than zero.");
        if (uploadExpiresAt <= DateTimeOffset.UtcNow) throw new ArgumentOutOfRangeException(nameof(uploadExpiresAt), "Media upload expiry must be in the future.");

        return new MediaAsset
        {
            Id = id ?? IdGenerator.NewId(),
            TenantId = Require(tenantId, nameof(tenantId), 26),
            ObjectKey = Require(objectKey, nameof(objectKey), ObjectKeyMaxLength),
            ContentType = Require(contentType, nameof(contentType), ContentTypeMaxLength).ToLowerInvariant(),
            ByteSize = byteSize,
            State = MediaAssetState.UploadPending,
            UploadExpiresAt = uploadExpiresAt
        };
    }

    public void Complete(DateTimeOffset now)
    {
        EnsureState(MediaAssetState.UploadPending);
        if (now > UploadExpiresAt) throw new InvalidOperationException("The media upload has expired.");
        State = MediaAssetState.Ready;
        ReadyAt = now;
    }

    public void AttachToProduct(string productId, int sortOrder, string? altText)
    {
        EnsureState(MediaAssetState.Ready);
        if (sortOrder < 0) throw new ArgumentOutOfRangeException(nameof(sortOrder), "Media sort order cannot be negative.");
        ProductId = Require(productId, nameof(productId), 26);
        SortOrder = sortOrder;
        AltText = Optional(altText, AltTextMaxLength);
    }

    public void Reorder(int sortOrder, string? altText)
    {
        EnsureState(MediaAssetState.Ready);
        if (ProductId is null) throw new InvalidOperationException("Only attached media can be reordered.");
        if (sortOrder < 0) throw new ArgumentOutOfRangeException(nameof(sortOrder), "Media sort order cannot be negative.");
        SortOrder = sortOrder;
        AltText = Optional(altText, AltTextMaxLength);
    }

    public void RequestDeletion(DateTimeOffset now)
    {
        if (State is MediaAssetState.Deleted or MediaAssetState.DeletionPending) return;
        State = MediaAssetState.DeletionPending;
        DeletionRequestedAt = now;
    }

    public void MarkDeleted(DateTimeOffset now)
    {
        EnsureState(MediaAssetState.DeletionPending);
        State = MediaAssetState.Deleted;
        DeletedAt = now;
        ProductId = null;
        SortOrder = null;
        AltText = null;
    }

    private void EnsureState(MediaAssetState state)
    {
        if (State != state) throw new InvalidOperationException($"Media asset must be {state}.");
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
