namespace Kreyora.Domain.Integrations;

public sealed record EncryptedSecret(
    string CiphertextBase64,
    string IvBase64,
    string AuthTagBase64,
    string KeyVersion)
{
    public static EncryptedSecret Create(string ciphertextBase64, string ivBase64, string authTagBase64, string keyVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertextBase64);
        ArgumentException.ThrowIfNullOrWhiteSpace(ivBase64);
        ArgumentException.ThrowIfNullOrWhiteSpace(authTagBase64);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyVersion);

        return new EncryptedSecret(ciphertextBase64, ivBase64, authTagBase64, keyVersion);
    }
}

