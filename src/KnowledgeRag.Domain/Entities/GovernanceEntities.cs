namespace KnowledgeRag.Domain.Entities;

public sealed class KnowledgeBaseRagProfileEntity
{
    public Guid KnowledgeBaseId { get; set; }
    public bool EnablePlanner { get; set; } = true;
    public int PlannerMaxQueries { get; set; } = 3;
    public int MultiQueryTopK { get; set; } = 6;
    public int MaxMergedEvidence { get; set; } = 10;
    public bool EnableReviewer { get; set; } = true;
    public int ReviewerMaxRetries { get; set; } = 1;
    public double ReviewerMinConfidence { get; set; } = 0.65;
    // Legacy raw model overrides remain for backward compatibility when no deployment binding exists.
    public string PlannerModel { get; set; } = string.Empty;
    public string AnswerModel { get; set; } = string.Empty;
    public string ReviewerModel { get; set; } = string.Empty;
    public Guid? PlannerDeploymentId { get; set; }
    public Guid? AnswerDeploymentId { get; set; }
    public Guid? ReviewerDeploymentId { get; set; }
    public Guid? RerankerDeploymentId { get; set; }
    public Guid? PromptProfileId { get; set; }
    public string AgentMode { get; set; } = "specialists";
    public decimal MonthlyBudgetUsd { get; set; }
    public int DailyRunLimit { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class RagRunEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid KnowledgeBaseId { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid UserId { get; set; }
    public Guid? ApiKeyId { get; set; }
    public string Query { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string Status { get; set; } = "completed";
    public string AnswerModel { get; set; } = string.Empty;
    public Guid? AnswerDeploymentId { get; set; }
    public string AgentMode { get; set; } = "specialists";
    public int SourceCount { get; set; }
    public int EstimatedInputTokens { get; set; }
    public int EstimatedOutputTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public long LatencyMs { get; set; }
    public string TraceJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ModelPriceEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    /// <summary>Preferred identity for provider/deployment-specific pricing. Null preserves legacy model-name pricing.</summary>
    public Guid? DeploymentId { get; set; }
    public string Model { get; set; } = string.Empty;
    public decimal InputCostPerMillionTokens { get; set; }
    public decimal OutputCostPerMillionTokens { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AuditLogEntity
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid? ApiKeyId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string? ResourceId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? DetailsJson { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public int StatusCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PromptProfileEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? KnowledgeBaseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string PlannerPrompt { get; set; } = string.Empty;
    public string AnswerPrompt { get; set; } = string.Empty;
    public string ReviewerPrompt { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class WebhookSubscriptionEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = "custom";
    public string Url { get; set; } = string.Empty;
    public string EventTypes { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int? LastStatusCode { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LastDeliveredAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EvaluationDatasetEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid KnowledgeBaseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EvaluationCaseEntity
{
    public Guid Id { get; set; }
    public Guid DatasetId { get; set; }
    public string Category { get; set; } = "fact";
    public string Query { get; set; } = string.Empty;
    public string? ExpectedAnswerContains { get; set; }
    public bool RequiresKnowledge { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class EvaluationRunEntity
{
    public Guid Id { get; set; }
    public Guid DatasetId { get; set; }
    public Guid KnowledgeBaseId { get; set; }
    public string Status { get; set; } = "running";
    public int CaseCount { get; set; }
    public int PassedCount { get; set; }
    public double PassRate { get; set; }
    public double GroundednessRate { get; set; }
    public double AverageLatencyMs { get; set; }
    public double AverageSourceCount { get; set; }
    public string ResultsJson { get; set; } = "[]";
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
