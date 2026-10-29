using System.Text;
using KnowledgeRag.Infrastructure.Configuration;

namespace KnowledgeRag.Api.Extensions;

public static class SecurityConfigurationValidator
{
    public static void Validate(IConfiguration configuration, bool isProduction)
    {
        var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        var signingKey = auth.SigningKey ?? string.Empty;
        if (Encoding.UTF8.GetByteCount(signingKey) < 32)
            throw new InvalidOperationException("Auth:SigningKey 至少需要 32 字节。");

        var secretProtection = configuration.GetSection(SecretProtectionOptions.SectionName).Get<SecretProtectionOptions>()
                               ?? new SecretProtectionOptions();
        var masterKey = secretProtection.MasterKey ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(masterKey) && Encoding.UTF8.GetByteCount(masterKey) < 32)
            throw new InvalidOperationException("SecretProtection:MasterKey 配置后至少需要 32 字节。");

        if (!isProduction) return;

        if (IsKnownPlaceholder(signingKey))
            throw new InvalidOperationException("Production 环境拒绝公开占位 Auth:SigningKey；请从 Secret Store 注入随机密钥。");
        if (Encoding.UTF8.GetByteCount(masterKey) < 32)
            throw new InvalidOperationException("Production 环境必须配置独立的 SecretProtection:MasterKey（至少 32 字节）。");
        if (IsKnownPlaceholder(masterKey))
            throw new InvalidOperationException("Production 环境拒绝 SecretProtection:MasterKey 占位值；请从 Secret Store 注入独立随机密钥。");
        if (string.Equals(signingKey, masterKey, StringComparison.Ordinal))
            throw new InvalidOperationException("Auth:SigningKey 与 SecretProtection:MasterKey 必须使用不同密钥。");
    }

    private static bool IsKnownPlaceholder(string value)
        => string.IsNullOrWhiteSpace(value)
           || value.Contains("CHANGE-ME-IN-PRODUCTION", StringComparison.OrdinalIgnoreCase)
           || value.Contains("PLEASE-CHANGE-THIS-TO-A-LONG-RANDOM-SECRET", StringComparison.OrdinalIgnoreCase)
           || value.Contains("REPLACE-WITH", StringComparison.OrdinalIgnoreCase)
           || value.Contains("RANDOM-SECRET", StringComparison.OrdinalIgnoreCase)
           || value.Contains('<')
           || value.Contains('>');
}
