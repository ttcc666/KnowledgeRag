namespace KnowledgeRag.Infrastructure.Configuration;

public sealed class SecretProtectionOptions
{
    public const string SectionName = "SecretProtection";

    /// <summary>
    /// Dedicated master key for encrypting persisted application secrets. In production this
    /// should be supplied through a secret store or environment variable rather than source control.
    /// When empty, the Auth signing key is used only as a backwards-compatible development fallback.
    /// </summary>
    public string MasterKey { get; set; } = string.Empty;
}
