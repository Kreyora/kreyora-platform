using Kreyora.Application.Models;
using Kreyora.Domain.Catalog;

namespace Kreyora.Application.Catalog;

public interface IMediaAssetService
{
    Task<Result<MediaAssetItem>> InitiateUploadAsync(InitiateMediaUploadRequest request, CancellationToken cancellationToken = default);
    Task<Result<MediaAssetItem>> CompleteUploadAsync(CompleteMediaUploadRequest request, Stream content, CancellationToken cancellationToken = default);
    Task<Result<MediaAssetItem>> AttachToProductAsync(AttachMediaToProductRequest request, CancellationToken cancellationToken = default);
    Task<Result<MediaAssetItem>> ReorderAsync(ReorderMediaRequest request, CancellationToken cancellationToken = default);
    Task<Result<MediaAssetItem>> RequestDeletionAsync(string mediaAssetId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<MediaAssetItem>>> ListForProductAsync(string productId, CancellationToken cancellationToken = default);
    Task<Result<MediaReadContent>> OpenReadAsync(string mediaAssetId, CancellationToken cancellationToken = default);
    Task<int> CleanupAsync(CancellationToken cancellationToken = default);
}

public sealed record InitiateMediaUploadRequest(string ContentType, long ByteSize);
public sealed record CompleteMediaUploadRequest(string MediaAssetId);
public sealed record AttachMediaToProductRequest(string MediaAssetId, string ProductId, int SortOrder, string? AltText);
public sealed record ReorderMediaRequest(string MediaAssetId, int SortOrder, string? AltText);
public sealed record MediaAssetItem(string Id, string? ProductId, string ContentType, long ByteSize, MediaAssetState State, int? SortOrder, string? AltText, DateTimeOffset UploadExpiresAt);
public sealed record MediaReadContent(Stream Content, string ContentType, long ByteSize);

public sealed record StorageObjectWrite(string ObjectKey, string ContentType, long ByteSize, Stream Content);
public sealed record StorageObjectMetadata(string ObjectKey, string ContentType, long ByteSize);

public interface IPrivateObjectStorage
{
    Task PutAsync(StorageObjectWrite request, CancellationToken cancellationToken = default);
    Task<StorageObjectMetadata?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken = default);
    Task DeleteIfExistsAsync(string objectKey, CancellationToken cancellationToken = default);
}
