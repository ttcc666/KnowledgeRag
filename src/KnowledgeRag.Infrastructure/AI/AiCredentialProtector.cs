using System.Security.Cryptography;
using System.Text;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.AI;

public sealed class AiCredentialProtector
{
    private const string Prefix = "enc:ai:v1:";
    private readonly byte[] _key;
    private readonly IReadOnlyList<byte[]> _decryptionKeys;

    public AiCredentialProtector(IOptions<SecretProtectionOptions> secretOptions, IOptions<AuthOptions> authOptions)
    {
        var secrets = secretOptions.Value;
        var auth = authOptions.Value;
        var master = string.IsNullOrWhiteSpace(secrets.MasterKey) ? auth.SigningKey : secrets.MasterKey;
        if (string.IsNullOrWhiteSpace(master))
            throw new InvalidOperationException("SecretProtection:MasterKey 未配置，且没有可用于开发兼容的 Auth:SigningKey。");

        _key = DeriveKey(master);
        var keys = new List<byte[]> { _key };
        foreach (var previous in secrets.PreviousMasterKeys.Where(x => !string.IsNullOrWhiteSpace(x)))
            AddUnique(keys, DeriveKey(previous));
        AddUnique(keys, DeriveKey(auth.SigningKey));
        foreach (var legacy in secrets.LegacyAuthSigningKeys.Where(x => !string.IsNullOrWhiteSpace(x)))
            AddUnique(keys, DeriveKey(legacy));
        _decryptionKeys = keys;
    }

    public string Protect(string plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext)) return string.Empty;
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var source = Encoding.UTF8.GetBytes(plaintext.Trim());
        var cipher = new byte[source.Length];
        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, source, cipher, tag);
        var payload = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, payload, nonce.Length + tag.Length, cipher.Length);
        return Prefix + Convert.ToBase64String(payload);
    }

    public string Unprotect(string protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue)) return string.Empty;
        if (!protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
            throw new CryptographicException("Unsupported AI credential format.");

        var payload = Convert.FromBase64String(protectedValue[Prefix.Length..]);
        if (payload.Length < 28) throw new CryptographicException("Invalid AI credential payload.");
        var nonce = payload[..12];
        var tag = payload[12..28];
        var cipher = payload[28..];

        foreach (var key in _decryptionKeys)
        {
            var plaintext = new byte[cipher.Length];
            try
            {
                using var aes = new AesGcm(key, tag.Length);
                aes.Decrypt(nonce, cipher, tag, plaintext);
                return Encoding.UTF8.GetString(plaintext);
            }
            catch (CryptographicException)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        throw new CryptographicException("AI credential could not be decrypted with the configured current or previous keys.");
    }

    private static byte[] DeriveKey(string master)
        => SHA256.HashData(Encoding.UTF8.GetBytes("KnowledgeRag.AiProviderCredential.v1\n" + master));

    private static void AddUnique(List<byte[]> keys, byte[] candidate)
    {
        if (!keys.Any(existing => CryptographicOperations.FixedTimeEquals(existing, candidate)))
            keys.Add(candidate);
    }
}
