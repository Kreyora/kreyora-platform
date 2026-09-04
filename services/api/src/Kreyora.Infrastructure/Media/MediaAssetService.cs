using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Catalog;
using Kreyora.Application.Models;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Catalog;
using Kreyora.Domain.Abstractions;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Media;

public sealed class MediaAssetService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    ITenantPermissionAuthorizer permissionAuthorizer,
    ITenantKeyBuilder tenantKeys,
    IPrivateObjectStorage storage,
    IAuditEventService auditEvents,
    Domain.Abstractions.ITimeProvider timeProvider,
    IOptions<MediaStorageOptions> options) : IMediaAssetService
{
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.Ordinal)
    {
        ["image/jpeg"] = "jpg",
        ["image/png"] = "png",
        ["image/webp"] = "webp"
    };

    public async Task<Result<MediaAssetItem>> InitiateUploadAsync(InitiateMediaUploadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.CatalogWrite);
        var context = tenantContext.RequireCurrent();
        try
        {
            var contentType = NormalizeContentType(request.ContentType);
            ValidateSize(request.ByteSize);
            var assetId = IdGenerator.NewId();
            var objectKey = tenantKeys.BuildStorageObjectKey("media", assetId, $"original.{Extensions[contentType]}");
            var asset = MediaAsset.CreatePending(context.TenantId, objectKey, contentType, request.ByteSize, timeProvider.UtcNow.Add(options.Value.UploadLifetime), assetId);
            dbContext.MediaAssets.Add(asset);
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditEvents.AppendAsync(new AuditEventWrite("media.upload.initiated", "media-asset", asset.Id,
                Metadata: $"{{\"contentType\":\"{asset.ContentType}\",\"byteSize\":{asset.ByteSize}}}"), cancellationToken);
            return Result<MediaAssetItem>.Success(Map(asset));
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return Result<MediaAssetItem>.ValidationError(exception.Message);
        }
    }

    public async Task<Result<MediaAssetItem>> CompleteUploadAsync(CompleteMediaUploadRequest request, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(content);
        permissionAuthorizer.Demand(TenantPermissions.CatalogWrite);
        var asset = await dbContext.MediaAssets.SingleOrDefaultAsync(item => item.Id == request.MediaAssetId, cancellationToken);
        if (asset is null) return Result<MediaAssetItem>.NotFound("The media asset does not exist in the selected workspace.");
        try
        {
            if (timeProvider.UtcNow > asset.UploadExpiresAt) throw new InvalidOperationException("The media upload has expired.");
            var bytes = await ReadExactlyAsync(content, asset.ByteSize, cancellationToken);
            if (!HasExpectedSignature(asset.ContentType, bytes)) throw new ArgumentException("Media content does not match its approved image type.");
            await using var upload = new MemoryStream(bytes, writable: false);
            await storage.PutAsync(new StorageObjectWrite(asset.ObjectKey, asset.ContentType, asset.ByteSize, upload), cancellationToken);
            asset.Complete(timeProvider.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditEvents.AppendAsync(new AuditEventWrite("media.upload.completed", "media-asset", asset.Id,
                Metadata: $"{{\"contentType\":\"{asset.ContentType}\",\"byteSize\":{asset.ByteSize}}}"), cancellationToken);
            return Result<MediaAssetItem>.Success(Map(asset));
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            dbContext.ChangeTracker.Clear();
            return Result<MediaAssetItem>.ValidationError(exception.Message);
        }
    }

    public async Task<Result<MediaAssetItem>> AttachToProductAsync(AttachMediaToProductRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.CatalogWrite);
        var asset = await dbContext.MediaAssets.SingleOrDefaultAsync(item => item.Id == request.MediaAssetId, cancellationToken);
        var product = await dbContext.Products.SingleOrDefaultAsync(item => item.Id == request.ProductId, cancellationToken);
        if (asset is null || product is null) return Result<MediaAssetItem>.NotFound("The media asset or product does not exist in the selected workspace.");
        try
        {
            asset.AttachToProduct(product.Id, request.SortOrder, request.AltText);
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditEvents.AppendAsync(new AuditEventWrite("media.attached", "media-asset", asset.Id,
                Metadata: $"{{\"productId\":\"{product.Id}\",\"sortOrder\":{asset.SortOrder}}}"), cancellationToken);
            return Result<MediaAssetItem>.Success(Map(asset));
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            dbContext.ChangeTracker.Clear();
            return Result<MediaAssetItem>.ValidationError(exception.Message);
        }
        catch (DbUpdateException)
        {
            return Result<MediaAssetItem>.Conflict("Another media asset already uses this product position.");
        }
    }

    public async Task<Result<MediaAssetItem>> ReorderAsync(ReorderMediaRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.CatalogWrite);
        var asset = await dbContext.MediaAssets.SingleOrDefaultAsync(item => item.Id == request.MediaAssetId, cancellationToken);
        if (asset is null) return Result<MediaAssetItem>.NotFound("The media asset does not exist in the selected workspace.");
        try
        {
            asset.Reorder(request.SortOrder, request.AltText);
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditEvents.AppendAsync(new AuditEventWrite("media.reordered", "media-asset", asset.Id,
                Metadata: $"{{\"productId\":\"{asset.ProductId}\",\"sortOrder\":{asset.SortOrder}}}"), cancellationToken);
            return Result<MediaAssetItem>.Success(Map(asset));
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            dbContext.ChangeTracker.Clear();
            return Result<MediaAssetItem>.ValidationError(exception.Message);
        }
        catch (DbUpdateException)
        {
            return Result<MediaAssetItem>.Conflict("Another media asset already uses this product position.");
        }
    }

    public async Task<Result<MediaAssetItem>> RequestDeletionAsync(string mediaAssetId, CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.CatalogWrite);
        var asset = await dbContext.MediaAssets.SingleOrDefaultAsync(item => item.Id == mediaAssetId, cancellationToken);
        if (asset is null) return Result<MediaAssetItem>.NotFound("The media asset does not exist in the selected workspace.");
        asset.RequestDeletion(timeProvider.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditEvents.AppendAsync(new AuditEventWrite("media.deletion.requested", "media-asset", asset.Id), cancellationToken);
        return Result<MediaAssetItem>.Success(Map(asset));
    }

    public async Task<Result<IReadOnlyList<MediaAssetItem>>> ListForProductAsync(string productId, CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.CatalogRead);
        var items = await dbContext.MediaAssets.AsNoTracking().Where(item => item.ProductId == productId && item.State == MediaAssetState.Ready)
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Id).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<MediaAssetItem>>.Success(items.Select(Map).ToArray());
    }

    public async Task<Result<MediaReadContent>> OpenReadAsync(string mediaAssetId, CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.CatalogRead);
        var asset = await dbContext.MediaAssets.AsNoTracking().SingleOrDefaultAsync(item => item.Id == mediaAssetId && item.State == MediaAssetState.Ready, cancellationToken);
        if (asset is null) return Result<MediaReadContent>.NotFound("The private media asset does not exist in the selected workspace.");
        var content = await storage.OpenReadAsync(asset.ObjectKey, cancellationToken);
        return content is null
            ? Result<MediaReadContent>.NotFound("The private media object is unavailable.")
            : Result<MediaReadContent>.Success(new MediaReadContent(content, asset.ContentType, asset.ByteSize));
    }

    public async Task<int> CleanupAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.UtcNow;
        var candidates = await dbContext.MediaAssets.Where(item =>
                (item.State == MediaAssetState.UploadPending && item.UploadExpiresAt <= now)
                || item.State == MediaAssetState.DeletionPending
                || (item.State == MediaAssetState.Ready && item.ProductId == null && item.ReadyAt <= now.AddHours(-24)))
            .OrderBy(item => item.CreatedAt).Take(100).ToListAsync(cancellationToken);
        var count = 0;
        foreach (var asset in candidates)
        {
            if (asset.State != MediaAssetState.DeletionPending)
            {
                asset.RequestDeletion(now);
                await dbContext.SaveChangesAsync(cancellationToken);
                await auditEvents.AppendAsync(new AuditEventWrite("media.deletion.requested", "media-asset", asset.Id, ActorUserId: null), cancellationToken);
            }

            await storage.DeleteIfExistsAsync(asset.ObjectKey, cancellationToken);
            asset.MarkDeleted(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditEvents.AppendAsync(new AuditEventWrite("media.deleted", "media-asset", asset.Id, ActorUserId: null), cancellationToken);
            count++;
        }

        return count;
    }

    private static MediaAssetItem Map(MediaAsset asset) => new(asset.Id, asset.ProductId, asset.ContentType, asset.ByteSize, asset.State, asset.SortOrder, asset.AltText, asset.UploadExpiresAt);

    private static string NormalizeContentType(string contentType)
    {
        var normalized = string.IsNullOrWhiteSpace(contentType) ? throw new ArgumentException("Media content type is required.", nameof(contentType)) : contentType.Trim().ToLowerInvariant();
        return Extensions.ContainsKey(normalized) ? normalized : throw new ArgumentException("Only JPEG, PNG, and WebP product images are allowed.", nameof(contentType));
    }

    private void ValidateSize(long byteSize)
    {
        if (byteSize <= 0 || byteSize > options.Value.MaxUploadBytes) throw new ArgumentOutOfRangeException(nameof(byteSize), "Media size exceeds the allowed limit.");
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream content, long expectedSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(expectedSize, (long)int.MaxValue);
        await using var buffer = new MemoryStream((int)expectedSize);
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length != expectedSize) throw new ArgumentException("Media upload size does not match the approved size.");
        return buffer.ToArray();
    }

    private static bool HasExpectedSignature(string contentType, ReadOnlySpan<byte> bytes) => contentType switch
    {
        "image/jpeg" => bytes.Length >= 3 && bytes[..3].SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF }),
        "image/png" => bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        "image/webp" => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8),
        _ => false
    };
}
