namespace KnowledgeRag.Application.Contracts;

public sealed record AiProviderDescriptor(string Id, string Name, string Description, bool SupportsCustomBaseUrl);

public sealed record AiProviderConnectionSummary(
    Guid Id,
    Guid TenantId,
    string Name,
    string ProviderType,
    string BaseUrl,
    bool HasCredential,
    bool IsEnabled,
    int Revision,
    DateTimeOffset UpdatedAt);

public sealed record SaveAiProviderConnectionRequest(
    Guid? Id,
    string Name,
    string ProviderType,
    string BaseUrl,
    string? Credential,
    bool IsEnabled = true);

public sealed record AiModelDeploymentSummary(
    Guid Id,
    Guid TenantId,
    Guid ConnectionId,
    string ConnectionName,
    string DisplayName,
    string RemoteModelName,
    string ModelType,
    int? EmbeddingDimensions,
    bool IsEnabled,
    DateTimeOffset UpdatedAt);

public sealed record SaveAiModelDeploymentRequest(
    Guid? Id,
    Guid ConnectionId,
    string DisplayName,
    string RemoteModelName,
    string ModelType,
    int? EmbeddingDimensions,
    bool IsEnabled = true);

public sealed record TenantAiDefaultsSummary(
    Guid TenantId,
    Guid? DefaultChatDeploymentId,
    Guid? DefaultEmbeddingDeploymentId,
    Guid? DefaultRerankerDeploymentId,
    DateTimeOffset? UpdatedAt);

public sealed record SaveTenantAiDefaultsRequest(
    Guid? DefaultChatDeploymentId,
    Guid? DefaultEmbeddingDeploymentId,
    Guid? DefaultRerankerDeploymentId);

public sealed record AiConnectionTestResult(bool Success, string Message);
