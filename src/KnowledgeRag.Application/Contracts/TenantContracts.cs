using KnowledgeRag.Domain.Entities;

namespace KnowledgeRag.Application.Contracts;

public sealed record CreateTenantRequest(string Name);
public sealed record UpdateTenantRequest(string Name);
public sealed record AddTenantMemberRequest(string Email, TenantRole Role);
public sealed record TenantSummary(Guid Id, string Name, string Slug, TenantRole Role, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record TenantMemberSummary(Guid Id, string Email, string DisplayName, TenantRole Role, DateTimeOffset AddedAt);

public sealed record CreateApiKeyRequest(string Name, string[] Scopes, Guid? KnowledgeBaseId = null, DateTimeOffset? ExpiresAt = null);
public sealed record ApiKeySummary(Guid Id, string Name, string KeyPrefix, string[] Scopes, Guid? KnowledgeBaseId, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, DateTimeOffset? LastUsedAt);
public sealed record CreatedApiKeyResponse(Guid Id, string Name, string ApiKey, string[] Scopes, Guid? KnowledgeBaseId, DateTimeOffset? ExpiresAt);
