using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Kreyora.Application.Catalog;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Media;

public sealed class LocalPrivateObjectStorage : IPrivateObjectStorage
{
    private readonly string root;

    public LocalPrivateObjectStorage(IOptions<MediaStorageOptions> options, IHostEnvironment environment)
    {
        root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.LocalRoot));
        Directory.CreateDirectory(root);
    }

    public async Task PutAsync(StorageObjectWrite request, CancellationToken cancellationToken = default)
    {
        var path = Resolve(request.ObjectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            await request.Content.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
            if (output.Length != request.ByteSize) throw new InvalidOperationException("Media upload size does not match the approved size.");
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public Task<StorageObjectMetadata?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        var path = Resolve(objectKey);
        return Task.FromResult(File.Exists(path)
            ? new StorageObjectMetadata(objectKey, ContentTypeFromKey(objectKey), new FileInfo(path).Length)
            : null);
    }

    public Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        var path = Resolve(objectKey);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteIfExistsAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        var path = Resolve(objectKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string Resolve(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey) || objectKey.Contains("..", StringComparison.Ordinal)
            || objectKey.Contains('\\')) throw new ArgumentException("Object key is unsafe.", nameof(objectKey));
        var path = Path.GetFullPath(Path.Combine(root, objectKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Object key escapes the private storage root.", nameof(objectKey));
        return path;
    }

    private static string ContentTypeFromKey(string objectKey) => Path.GetExtension(objectKey).ToLowerInvariant() switch
    {
        ".jpg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "application/octet-stream"
    };
}

public sealed class R2PrivateObjectStorage : IPrivateObjectStorage, IDisposable
{
    private readonly AmazonS3Client client;
    private readonly string bucketName;

    public R2PrivateObjectStorage(IOptions<MediaStorageOptions> options)
    {
        var r2 = options.Value.R2;
        bucketName = r2.BucketName;
        client = new AmazonS3Client(new BasicAWSCredentials(r2.AccessKeyId, r2.SecretAccessKey), new AmazonS3Config
        {
            ServiceURL = r2.Endpoint,
            ForcePathStyle = true
        });
    }

    public async Task PutAsync(StorageObjectWrite request, CancellationToken cancellationToken = default) =>
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = request.ObjectKey,
            ContentType = request.ContentType,
            InputStream = request.Content,
            AutoCloseStream = false
        }, cancellationToken);

    public async Task<StorageObjectMetadata?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await client.GetObjectMetadataAsync(bucketName, objectKey, cancellationToken);
            return new StorageObjectMetadata(objectKey, response.Headers.ContentType, response.Headers.ContentLength);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await client.GetObjectAsync(bucketName, objectKey, cancellationToken);
            var copy = new MemoryStream();
            await response.ResponseStream.CopyToAsync(copy, cancellationToken);
            copy.Position = 0;
            return copy;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteIfExistsAsync(string objectKey, CancellationToken cancellationToken = default) =>
        await client.DeleteObjectAsync(bucketName, objectKey, cancellationToken);

    public void Dispose() => client.Dispose();
}
