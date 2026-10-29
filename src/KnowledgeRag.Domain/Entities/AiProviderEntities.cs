namespace KnowledgeRag.Domain.Entities;

public static class AiProviderTypes
{
    public const string OpenAiCompatible = "openai-compatible";
}

public static class AiModelTypes
{
    public const string Chat = "chat";
    public const string Embedding = "embedding";
    public const string Rerank = "rerank";
}

public static class AiDeploymentHealthStatuses
{
    public const string Unknown = "unknown";
    public const string Healthy = "healthy";
    public const string Unhealthy = "unhealthy";
}

public sealed class AiProviderConnectionEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProviderType { get; set; } = AiProviderTypes.OpenAiCompatible;
    public string BaseUrl { get; set; } = string.Empty;
    public string EncryptedCredential { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = "{}";
    public bool IsEnabled { get; set; } = true;
    public int Revision { get; set; } = 1;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AiModelDeploymentEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ConnectionId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string RemoteModelName { get; set; } = string.Empty;
    public string ModelType { get; set; } = AiModelTypes.Chat;
    public int? EmbeddingDimensions { get; set; }
    public Guid? FallbackDeploymentId { get; set; }
    public string HealthStatus { get; set; } = AiDeploymentHealthStatuses.Unknown;
    public DateTimeOffset? LastHealthCheckedAt { get; set; }
    public string? LastHealthError { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TenantAiDefaultsEntity
{
    public Guid TenantId { get; set; }
    public Guid? DefaultChatDeploymentId { get; set; }
    public Guid? DefaultEmbeddingDeploymentId { get; set; }
    public Guid? DefaultRerankerDeploymentId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
