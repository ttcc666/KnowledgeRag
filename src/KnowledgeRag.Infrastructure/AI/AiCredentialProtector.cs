using System.Security.Cryptography;
using System.Text;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.AI;

public sealed class AiCredentialProtector
{
    private const string Prefix = "enc:ai:v1:";
    private readonly byte[] _key;

    public AiCredentialProtector(IOptions<SecretProtectionOptions> secretOptions, IOptions<AuthOptions> authOptions)
    {
        var master = string.IsNullOrWhiteSpace(secretOptions.Value.MasterKey)
            ? authOptions.Value.SigningKey
            : secretOptions.Value.MasterKey;
        if (string.IsNullOrWhiteSpace(master))
            throw new InvalidOperationException("SecretProtection:MasterKey 未配置，且没有可用于开发兼容的 Auth:SigningKey。");
        _key = SHA256.HashData(Encoding.UTF8.GetBytes("KnowledgeRag.AiProviderCredential.v1\n" + master));
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
        var plaintext = new byte[cipher.Length];
        using var aes = new AesGcm(_key, tag.Length);
        aes.Decrypt(nonce, cipher, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }
}
