using System.Security.Cryptography;
using Kreyora.Domain.Integrations;
using Kreyora.Infrastructure.Integrations;
using Microsoft.Extensions.Options;

namespace Kreyora.UnitTests.Integrations;

public sealed class AesGcmSecretEncryptionServiceTests
{
    [Fact]
    public void EncryptAndDecrypt_WithDefaultDevKey_RoundTripsSuccessfully()
    {
        var service = new AesGcmSecretEncryptionService();
        const string plainText = "EAABsbcsd8f76sdfsd87f6sd87fsd68fsd";

        var encrypted = service.Encrypt(plainText);

        Assert.NotNull(encrypted);
        Assert.NotEmpty(encrypted.CiphertextBase64);
        Assert.NotEmpty(encrypted.IvBase64);
        Assert.NotEmpty(encrypted.AuthTagBase64);
        Assert.Equal("v1", encrypted.KeyVersion);

        var decrypted = service.Decrypt(encrypted);
        Assert.Equal(plainText, decrypted);
    }

    [Fact]
    public void EncryptAndDecrypt_WithConfiguredMasterKey_RoundTripsSuccessfully()
    {
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var base64Key = Convert.ToBase64String(keyBytes);
        var options = Options.Create(new SecretEncryptionOptions
        {
            MasterKey = base64Key,
            DefaultKeyVersion = "2026-q1"
        });

        var service = new AesGcmSecretEncryptionService(options);
        const string plainText = "whsec_custom_webhook_secret_value_12345";

        var encrypted = service.Encrypt(plainText);

        Assert.Equal("2026-q1", encrypted.KeyVersion);
        var decrypted = service.Decrypt(encrypted);
        Assert.Equal(plainText, decrypted);
    }

    [Fact]
    public void Encrypt_WithExplicitKeyVersion_UsesSpecifiedVersion()
    {
        var key1 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var key2 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var options = Options.Create(new SecretEncryptionOptions
        {
            MasterKey = key1,
            DefaultKeyVersion = "v1",
            VersionedKeys = new Dictionary<string, string>
            {
                ["v1"] = key1,
                ["v2"] = key2
            }
        });

        var service = new AesGcmSecretEncryptionService(options);

        var encryptedV1 = service.Encrypt("secret-v1", "v1");
        var encryptedV2 = service.Encrypt("secret-v2", "v2");

        Assert.Equal("v1", encryptedV1.KeyVersion);
        Assert.Equal("v2", encryptedV2.KeyVersion);

        Assert.Equal("secret-v1", service.Decrypt(encryptedV1));
        Assert.Equal("secret-v2", service.Decrypt(encryptedV2));
    }

    [Fact]
    public void Decrypt_WithUnknownKeyVersion_ThrowsKeyNotFoundException()
    {
        var service = new AesGcmSecretEncryptionService();
        var encrypted = new EncryptedSecret(
            CiphertextBase64: Convert.ToBase64String(new byte[16]),
            IvBase64: Convert.ToBase64String(new byte[12]),
            AuthTagBase64: Convert.ToBase64String(new byte[16]),
            KeyVersion: "unknown-v99");

        var ex = Assert.Throws<KeyNotFoundException>(() => service.Decrypt(encrypted));
        Assert.Contains("unknown-v99", ex.Message);
    }

    [Fact]
    public void Decrypt_WithCorruptedCiphertext_ThrowsCryptographicException()
    {
        var service = new AesGcmSecretEncryptionService();
        var encrypted = service.Encrypt("sensitive-token");

        // Tamper with ciphertext
        var rawCiphertext = Convert.FromBase64String(encrypted.CiphertextBase64);
        rawCiphertext[0] ^= 0xFF;
        var tampered = encrypted with { CiphertextBase64 = Convert.ToBase64String(rawCiphertext) };

        Assert.ThrowsAny<CryptographicException>(() => service.Decrypt(tampered));
    }

    [Fact]
    public void Decrypt_WithCorruptedAuthTag_ThrowsCryptographicException()
    {
        var service = new AesGcmSecretEncryptionService();
        var encrypted = service.Encrypt("sensitive-token");

        // Tamper with auth tag
        var rawTag = Convert.FromBase64String(encrypted.AuthTagBase64);
        rawTag[0] ^= 0xFF;
        var tampered = encrypted with { AuthTagBase64 = Convert.ToBase64String(rawTag) };

        Assert.ThrowsAny<CryptographicException>(() => service.Decrypt(tampered));
    }

    [Fact]
    public void Constructor_WithInvalidKeyLength_ThrowsArgumentException()
    {
        var invalidKey = Convert.ToBase64String(new byte[16]); // 16 bytes instead of 32
        var options = Options.Create(new SecretEncryptionOptions
        {
            MasterKey = invalidKey
        });

        Assert.Throws<ArgumentException>(() => new AesGcmSecretEncryptionService(options));
    }

    [Fact]
    public void Encrypt_WithNull_ThrowsArgumentNullException()
    {
        var service = new AesGcmSecretEncryptionService();
        Assert.Throws<ArgumentNullException>(() => service.Encrypt(null!));
    }

    [Fact]
    public void Decrypt_WithNull_ThrowsArgumentNullException()
    {
        var service = new AesGcmSecretEncryptionService();
        Assert.Throws<ArgumentNullException>(() => service.Decrypt(null!));
    }
}
