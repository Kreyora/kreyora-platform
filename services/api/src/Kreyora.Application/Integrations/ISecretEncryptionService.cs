using Kreyora.Domain.Integrations;

namespace Kreyora.Application.Integrations;

public interface ISecretEncryptionService
{
    EncryptedSecret Encrypt(string plainText, string? keyVersion = null);
    string Decrypt(EncryptedSecret encryptedSecret);
}

