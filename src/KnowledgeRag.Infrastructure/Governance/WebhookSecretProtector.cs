using System.Security.Cryptography;
using System.Text;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.Governance;

public sealed class WebhookSecretProtector
{
    private const string LegacyPrefix = "enc:v1:";
    private const string Prefix = "enc:v2:";
    private readonly byte[] _key;
    private readonly IReadOnlyList<byte[]> _currentEncryptionKeys;
    private readonly IReadOnlyList<byte[]> _legacyEncryptionKeys;

    public WebhookSecretProtector(
        IOptions<SecretProtectionOptions> secretOptions,
        IOptions<AuthOptions> authOptions)
    {
        var secrets = secretOptions.Value;
        var auth = authOptions.Value;
        var master = string.IsNullOrWhiteSpace(secrets.MasterKey) ? auth.SigningKey : secrets.MasterKey;
        if (string.IsNullOrWhiteSpace(master))
            throw new InvalidOperationException("SecretProtection:MasterKey 未配置，且没有可用于开发兼容的 Auth:SigningKey。");

        _key = DeriveCurrentKey(master);
        var currentKeys = new List<byte[]> { _key };
        foreach (var previous in secrets.PreviousMasterKeys.Where(x => !string.IsNullOrWhiteSpace(x)))
            AddUnique(currentKeys, DeriveCurrentKey(previous));
        _currentEncryptionKeys = currentKeys;

        // enc:v1 was encrypted directly from Auth:SigningKey. Keep this decryption-only path
        // while deployments migrate old values; all new secrets use the dedicated master key.
        var legacyKeys = new List<byte[]> { DeriveLegacyKey(auth.SigningKey) };
        foreach (var legacy in secrets.LegacyAuthSigningKeys.Where(x => !string.IsNullOrWhiteSpace(x)))
            AddUnique(legacyKeys, DeriveLegacyKey(legacy));
        _legacyEncryptionKeys = legacyKeys;
    }

    public string Protect(string plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext)) return string.Empty;
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var source = Encoding.UTF8.GetBytes(plaintext);
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
        if (protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
            return Decrypt(protectedValue[Prefix.Length..], _currentEncryptionKeys, "Invalid protected webhook secret.");
        if (protectedValue.StartsWith(LegacyPrefix, StringComparison.Ordinal))
            return Decrypt(protectedValue[LegacyPrefix.Length..], _legacyEncryptionKeys, "Invalid legacy webhook secret.");

        // Backwards compatibility for secrets stored in plaintext before encryption was introduced.
        return protectedValue;
    }

    private static string Decrypt(string encodedPayload, IReadOnlyList<byte[]> keys, string error)
    {
        var payload = Convert.FromBase64String(encodedPayload);
        if (payload.Length < 28) throw new CryptographicException(error);
        var nonce = payload[..12];
        var tag = payload[12..28];
        var cipher = payload[28..];

        foreach (var key in keys)
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

        throw new CryptographicException(error);
    }

    private static byte[] DeriveCurrentKey(string master)
        => SHA256.HashData(Encoding.UTF8.GetBytes("KnowledgeRag.WebhookSecret.v2\n" + master));

    private static byte[] DeriveLegacyKey(string signingKey)
        => SHA256.HashData(Encoding.UTF8.GetBytes(signingKey));

    private static void AddUnique(List<byte[]> keys, byte[] candidate)
    {
        if (!keys.Any(existing => CryptographicOperations.FixedTimeEquals(existing, candidate)))
            keys.Add(candidate);
    }
}
