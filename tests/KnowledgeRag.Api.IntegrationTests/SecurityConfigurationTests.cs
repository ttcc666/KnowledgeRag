using KnowledgeRag.Api.Extensions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class SecurityConfigurationTests
{
    [Theory]
    [InlineData("CHANGE-ME-IN-PRODUCTION-AT-LEAST-32-CHARS")]
    [InlineData("please-change-this-to-a-long-random-secret-at-least-32-characters")]
    public void Production_RejectsKnownSigningKeyPlaceholders(string signingKey)
    {
        var configuration = Build(signingKey, "independent-secret-protection-master-key-long-enough");
        var exception = Assert.Throws<InvalidOperationException>(
            () => SecurityConfigurationValidator.Validate(configuration, isProduction: true));
        Assert.Contains("Auth:SigningKey", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<independent-strong-random-secret>")]
    [InlineData("replace-with-a-long-random-secret-value-123456")]
    public void Production_RejectsKnownMasterKeyPlaceholders(string masterKey)
    {
        var configuration = Build("independent-jwt-signing-key-0123456789-abcdefghijklmnopqrstuvwxyz", masterKey);
        var exception = Assert.Throws<InvalidOperationException>(
            () => SecurityConfigurationValidator.Validate(configuration, isProduction: true));
        Assert.Contains("SecretProtection:MasterKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_RequiresDedicatedMasterKey()
    {
        var configuration = Build("random-production-signing-key-0123456789-abcdef", "");
        var exception = Assert.Throws<InvalidOperationException>(
            () => SecurityConfigurationValidator.Validate(configuration, isProduction: true));
        Assert.Contains("SecretProtection:MasterKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_RejectsReusingJwtSigningKeyAsMasterKey()
    {
        const string key = "same-production-key-0123456789-abcdefghijklmnopqrstuvwxyz";
        var configuration = Build(key, key);
        var exception = Assert.Throws<InvalidOperationException>(
            () => SecurityConfigurationValidator.Validate(configuration, isProduction: true));
        Assert.Contains("必须使用不同密钥", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_AcceptsIndependentConfiguredKeys()
    {
        var configuration = Build(
            "production-jwt-signing-key-0123456789-ABCDEFGHIJKLMNOPQRSTUVWXYZ",
            "production-secret-master-key-9876543210-abcdefghijklmnopqrstuvwxyz");
        SecurityConfigurationValidator.Validate(configuration, isProduction: true);
    }

    [Fact]
    public void NonProduction_AllowsLegacyAuthKeyFallback()
    {
        var configuration = Build("CHANGE-ME-IN-PRODUCTION-AT-LEAST-32-CHARS", "");
        SecurityConfigurationValidator.Validate(configuration, isProduction: false);
    }

    private static IConfiguration Build(string signingKey, string masterKey)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:SigningKey"] = signingKey,
                ["SecretProtection:MasterKey"] = masterKey
            })
            .Build();
}
