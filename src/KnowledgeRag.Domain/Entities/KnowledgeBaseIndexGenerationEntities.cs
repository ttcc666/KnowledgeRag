namespace KnowledgeRag.Domain.Entities;

public static class KnowledgeBaseIndexGenerationStatuses
{
    public const string Building = "building";
    public const string Active = "active";
    public const string Failed = "failed";
    public const string Superseded = "superseded";
}

public static class KnowledgeBaseIndexGenerationDocumentStatuses
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Ready = "ready";
    public const string Failed = "failed";
}

/// <summary>
/// Immutable embedding-space snapshot used by one KB index generation.
/// Credentials are never stored here; a tenant connection is referenced only to retrieve its
/// current encrypted credential when the endpoint is used.
/// </summary>
public sealed class KnowledgeBaseIndexGenerationEntity
{
    public Guid Id { get; set; }
    public Guid KnowledgeBaseId { get; set; }
    public Guid TenantId { get; set; }
    public Guid? EmbeddingDeploymentId { get; set; }
    public string DeploymentDisplayName { get; set; } = string.Empty;
    public Guid? ProviderConnectionId { get; set; }
    public string ProviderType { get; set; } = "openai-compatible";
    public string BaseUrl { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public int Dimensions { get; set; }
    public int ConnectionRevision { get; set; }
    public DateTimeOffset? DeploymentUpdatedAt { get; set; }
    public string Status { get; set; } = KnowledgeBaseIndexGenerationStatuses.Building;
    public int ExpectedDocumentCount { get; set; }
    public int CompletedDocumentCount { get; set; }
    public int FailedDocumentCount { get; set; }
    public string ManifestHash { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
}

public sealed class KnowledgeBaseIndexGenerationDocumentEntity
{
    public Guid IndexGenerationId { get; set; }
    public Guid DocumentId { get; set; }
    public string SourceSha256 { get; set; } = string.Empty;
    public string Status { get; set; } = KnowledgeBaseIndexGenerationDocumentStatuses.Pending;
    public int ChunkCount { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
