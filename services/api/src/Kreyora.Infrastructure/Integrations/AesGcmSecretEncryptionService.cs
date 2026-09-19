using System.Security.Cryptography;
using System.Text;
using Kreyora.Application.Integrations;
using Kreyora.Domain.Integrations;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Integrations;

public sealed class AesGcmSecretEncryptionService : ISecretEncryptionService
{
    private const int KeySizeBytes = 32; // AES-256
    private const int NonceSizeBytes = 12; // Standard 96-bit nonce for AES-GCM
    private const int TagSizeBytes = 16; // Standard 128-bit authentication tag

    // Fallback deterministic development key used ONLY if no key is configured (e.g. unit/local tests)
    private static readonly byte[] FallbackDevKey = Encoding.UTF8.GetBytes("Kreyora-Dev-Master-Key-32Bytes!!");

    private readonly Dictionary<string, byte[]> keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly string defaultVersion;

    public AesGcmSecretEncryptionService(IOptions<SecretEncryptionOptions>? options = null)
    {
        var opts = options?.Value ?? new SecretEncryptionOptions();
        defaultVersion = string.IsNullOrWhiteSpace(opts.DefaultKeyVersion) ? "v1" : opts.DefaultKeyVersion;

        // Load master key for default version
        if (!string.IsNullOrWhiteSpace(opts.MasterKey))
        {
            var keyBytes = Convert.FromBase64String(opts.MasterKey);
            ValidateKeySize(keyBytes);
            keys[defaultVersion] = keyBytes;
        }
        else
        {
            keys[defaultVersion] = FallbackDevKey;
        }

        // Load any additional versioned keys
        if (opts.VersionedKeys != null)
        {
            foreach (var (version, base64Key) in opts.VersionedKeys)
            {
                if (!string.IsNullOrWhiteSpace(base64Key))
                {
                    var keyBytes = Convert.FromBase64String(base64Key);
                    ValidateKeySize(keyBytes);
                    keys[version] = keyBytes;
                }
            }
        }
    }

    public EncryptedSecret Encrypt(string plainText, string? keyVersion = null)
    {
        ArgumentNullException.ThrowIfNull(plainText);

        var version = string.IsNullOrWhiteSpace(keyVersion) ? defaultVersion : keyVersion;
        var key = ResolveKey(version);

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var iv = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var ciphertext = new byte[plainBytes.Length];
        var authTag = new byte[TagSizeBytes];

        using (var aesGcm = new AesGcm(key, TagSizeBytes))
        {
            aesGcm.Encrypt(iv, plainBytes, ciphertext, authTag);
        }

        return new EncryptedSecret(
            CiphertextBase64: Convert.ToBase64String(ciphertext),
            IvBase64: Convert.ToBase64String(iv),
            AuthTagBase64: Convert.ToBase64String(authTag),
            KeyVersion: version);
    }

    public string Decrypt(EncryptedSecret encryptedSecret)
    {
        ArgumentNullException.ThrowIfNull(encryptedSecret);
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedSecret.CiphertextBase64);
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedSecret.IvBase64);
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedSecret.AuthTagBase64);
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedSecret.KeyVersion);

        var key = ResolveKey(encryptedSecret.KeyVersion);

        var ciphertext = Convert.FromBase64String(encryptedSecret.CiphertextBase64);
        var iv = Convert.FromBase64String(encryptedSecret.IvBase64);
        var authTag = Convert.FromBase64String(encryptedSecret.AuthTagBase64);
        var plainBytes = new byte[ciphertext.Length];

        using (var aesGcm = new AesGcm(key, TagSizeBytes))
        {
            aesGcm.Decrypt(iv, ciphertext, authTag, plainBytes);
        }

        return Encoding.UTF8.GetString(plainBytes);
    }

    private byte[] ResolveKey(string version)
    {
        if (!keys.TryGetValue(version, out var key))
        {
            throw new KeyNotFoundException($"Encryption master key for version '{version}' was not found.");
        }
        return key;
    }

    private static void ValidateKeySize(byte[] keyBytes)
    {
        if (keyBytes.Length != KeySizeBytes)
        {
            throw new ArgumentException($"Master key must be exactly {KeySizeBytes} bytes (256 bits). Found {keyBytes.Length} bytes.");
        }
    }
}

