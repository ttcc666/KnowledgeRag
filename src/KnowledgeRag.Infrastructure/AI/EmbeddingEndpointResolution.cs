namespace KnowledgeRag.Infrastructure.AI;

public sealed record EmbeddingEndpointResolution(
    ResolvedAiEndpoint Endpoint,
    int Dimensions,
    bool SendDimensions,
    string DeploymentDisplayName,
    DateTimeOffset? DeploymentUpdatedAt);
