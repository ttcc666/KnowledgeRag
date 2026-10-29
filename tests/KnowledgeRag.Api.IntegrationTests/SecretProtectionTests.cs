using System.Security.Cryptography;
using System.Text;
using KnowledgeRag.Infrastructure.AI;
using KnowledgeRag.Infrastructure.Configuration;
using KnowledgeRag.Infrastructure.Governance;
using Microsoft.Extensions.Options;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class SecretProtectionTests
{
    [Fact]
    public void AiCredentialProtector_DecryptsPreviousMasterKeyDuringRotation()
    {
        const string oldMaster = "old-ai-master-key-0123456789-abcdefghijklmnopqrstuvwxyz";
        const string newMaster = "new-ai-master-key-9876543210-abcdefghijklmnopqrstuvwxyz";
        var auth = Options.Create(new AuthOptions { SigningKey = "jwt-signing-key-independent-from-master-0123456789" });
        var oldProtector = new AiCredentialProtector(
            Options.Create(new SecretProtectionOptions { MasterKey = oldMaster }), auth);
        var ciphertext = oldProtector.Protect("provider-api-key");
        var rotatedProtector = new AiCredentialProtector(
            Options.Create(new SecretProtectionOptions { MasterKey = newMaster, PreviousMasterKeys = [oldMaster] }), auth);

        Assert.Equal("provider-api-key", rotatedProtector.Unprotect(ciphertext));
        Assert.Equal("new-provider-api-key", rotatedProtector.Unprotect(rotatedProtector.Protect("new-provider-api-key")));
    }

    [Fact]
    public void AiCredentialProtector_DecryptsLegacyAuthDerivedCredentialAfterMasterKeyIsIntroduced()
    {
        const string oldAuthKey = "legacy-auth-signing-key-for-old-secret-encryption-0123456789";
        var legacyProtector = new AiCredentialProtector(
            Options.Create(new SecretProtectionOptions()),
            Options.Create(new AuthOptions { SigningKey = oldAuthKey }));
        var ciphertext = legacyProtector.Protect("legacy-provider-secret");
        var currentProtector = new AiCredentialProtector(
            Options.Create(new SecretProtectionOptions
            {
                MasterKey = "dedicated-master-key-0123456789-abcdefghijklmnopqrstuvwxyz",
                LegacyAuthSigningKeys = [oldAuthKey]
            }),
            Options.Create(new AuthOptions { SigningKey = "new-auth-signing-key-0123456789-abcdefghijklmnopqrstuvwxyz" }));

        Assert.Equal("legacy-provider-secret", currentProtector.Unprotect(ciphertext));
    }

    [Fact]
    public void WebhookSecretProtector_DecryptsPreviousMasterKeyDuringRotation()
    {
        const string oldMaster = "old-webhook-master-key-0123456789-abcdefghijklmnopqrstuvwxyz";
        const string newMaster = "new-webhook-master-key-9876543210-abcdefghijklmnopqrstuvwxyz";
        var auth = Options.Create(new AuthOptions { SigningKey = "jwt-signing-key-independent-from-master-0123456789" });
        var oldProtector = new WebhookSecretProtector(
            Options.Create(new SecretProtectionOptions { MasterKey = oldMaster }), auth);
        var ciphertext = oldProtector.Protect("webhook-hmac-secret");
        var rotatedProtector = new WebhookSecretProtector(
            Options.Create(new SecretProtectionOptions { MasterKey = newMaster, PreviousMasterKeys = [oldMaster] }), auth);

        Assert.StartsWith("enc:v2:", rotatedProtector.Protect("next-secret"), StringComparison.Ordinal);
        Assert.Equal("webhook-hmac-secret", rotatedProtector.Unprotect(ciphertext));
    }

    [Fact]
    public void WebhookSecretProtector_DecryptsLegacyAuthKeyCiphertextAfterAuthRotation()
    {
        const string oldAuthKey = "legacy-auth-signing-key-for-webhook-encryption-0123456789";
        var legacyCiphertext = CreateLegacyWebhookCiphertext(oldAuthKey, "legacy-webhook-secret");
        var protector = new WebhookSecretProtector(
            Options.Create(new SecretProtectionOptions
            {
                MasterKey = "dedicated-master-key-0123456789-abcdefghijklmnopqrstuvwxyz",
                LegacyAuthSigningKeys = [oldAuthKey]
            }),
            Options.Create(new AuthOptions { SigningKey = "new-auth-signing-key-0123456789-abcdefghijklmnopqrstuvwxyz" }));

        Assert.Equal("legacy-webhook-secret", protector.Unprotect(legacyCiphertext));
    }

    private static string CreateLegacyWebhookCiphertext(string signingKey, string plaintext)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(signingKey));
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var source = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[source.Length];
        using (var aes = new AesGcm(key, tag.Length)) aes.Encrypt(nonce, source, cipher, tag);
        var payload = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, payload, nonce.Length + tag.Length, cipher.Length);
        return "enc:v1:" + Convert.ToBase64String(payload);
    }
}
