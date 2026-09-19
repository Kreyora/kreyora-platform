namespace Kreyora.Infrastructure.Integrations;

public sealed class SecretEncryptionOptions
{
    public const string SectionName = "SecretEncryption";

    /// <summary>
    /// Base64-encoded 256-bit (32-byte) master key for the default key version.
    /// </summary>
    public string? MasterKey { get; set; }

    /// <summary>
    /// Additional versioned keys for rotation support. Key is the version name (e.g. "v1", "v2").
    /// Value is the Base64-encoded 256-bit key.
    /// </summary>
    public Dictionary<string, string> VersionedKeys { get; set; } = [];

    /// <summary>
    /// Default key version to use when encrypting new secrets.
    /// </summary>
    public string DefaultKeyVersion { get; set; } = "v1";
}

