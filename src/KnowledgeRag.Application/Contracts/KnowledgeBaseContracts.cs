using KnowledgeRag.Domain.Entities;

namespace KnowledgeRag.Application.Contracts;

public sealed record CreateKnowledgeBaseRequest(string Name, string? Description = null, Guid? TenantId = null);
public sealed record UpdateKnowledgeBaseRequest(string Name, string? Description = null);
public sealed record AddKnowledgeBaseMemberRequest(string Email, KnowledgeBaseRole Role);
public sealed record KnowledgeBaseSummary(Guid Id, Guid TenantId, string Name, string? Description, KnowledgeBaseRole Role, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record KnowledgeBaseMemberSummary(Guid Id, string Email, string DisplayName, KnowledgeBaseRole Role, DateTimeOffset AddedAt);
public sealed record UploadResponse(Guid DocumentId, string FileName, int ChunkCount, string Sha256, DocumentStatus Status);
