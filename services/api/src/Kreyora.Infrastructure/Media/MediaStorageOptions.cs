using System.ComponentModel.DataAnnotations;

namespace Kreyora.Infrastructure.Media;

public sealed class MediaStorageOptions
{
    public const string SectionName = "Storage:Media";

    [Required]
    [RegularExpression("^(Local|R2)$")]
    public string Provider { get; init; } = "Local";

    [Required]
    public string LocalRoot { get; init; } = "AppData/private-media";

    [Range(1, 10 * 1024 * 1024)]
    public long MaxUploadBytes { get; init; } = 10 * 1024 * 1024;

    [Range(1, 60)]
    public int UploadLifetimeMinutes { get; init; } = 15;

    [Range(1, 60)]
    public int ReadLifetimeMinutes { get; init; } = 5;

    public R2StorageOptions R2 { get; init; } = new();

    public TimeSpan UploadLifetime => TimeSpan.FromMinutes(UploadLifetimeMinutes);

    public bool IsValidForEnvironment(bool isDevelopment) =>
        Provider switch
        {
            "Local" => isDevelopment,
            "R2" => R2.IsValid(),
            _ => false
        };
}

public sealed class R2StorageOptions
{
    public string AccountId { get; init; } = string.Empty;
    public string BucketName { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public string AccessKeyId { get; init; } = string.Empty;
    public string SecretAccessKey { get; init; } = string.Empty;

    public bool IsValid() => !string.IsNullOrWhiteSpace(AccountId)
        && !string.IsNullOrWhiteSpace(BucketName)
        && Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint)
        && endpoint.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrWhiteSpace(AccessKeyId)
        && !string.IsNullOrWhiteSpace(SecretAccessKey);
}
