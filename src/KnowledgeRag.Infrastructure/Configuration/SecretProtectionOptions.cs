namespace KnowledgeRag.Infrastructure.Configuration;

public sealed class SecretProtectionOptions
{
    public const string SectionName = "SecretProtection";

    /// <summary>
    /// Dedicated master key for persisted AI and webhook secrets. Production must provide a
    /// random value from a secret store; an empty value only permits the legacy development fallback.
    /// </summary>
    public string MasterKey { get; set; } = string.Empty;

    /// <summary>
    /// Previous master keys accepted for decryption during a controlled key rotation.
    /// New secrets are always encrypted with MasterKey.
    /// </summary>
    public string[] PreviousMasterKeys { get; set; } = [];

    /// <summary>
    /// Old Auth signing keys retained only to decrypt legacy secrets encrypted with Auth:SigningKey.
    /// Remove each value after all legacy secrets have been re-entered or re-encrypted.
    /// </summary>
    public string[] LegacyAuthSigningKeys { get; set; } = [];
}
