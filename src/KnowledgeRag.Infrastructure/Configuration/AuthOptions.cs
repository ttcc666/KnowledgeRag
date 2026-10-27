namespace KnowledgeRag.Infrastructure.Configuration;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string Issuer { get; set; } = "KnowledgeRag.NonAzure";
    public string Audience { get; set; } = "KnowledgeRag.NonAzure";
    public string SigningKey { get; set; } = "CHANGE-ME-IN-PRODUCTION-AT-LEAST-32-CHARS";
    public int AccessTokenLifetimeMinutes { get; set; } = 60;
    public int RefreshTokenLifetimeDays { get; set; } = 30;
    public bool AllowRegistration { get; set; } = true;
}
