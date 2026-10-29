using KnowledgeRag.Domain.Entities;

namespace KnowledgeRag.Application.Contracts;

public sealed record DocumentIndexRequested(Guid DocumentId, Guid KnowledgeBaseId, Guid UserId, Guid? IndexGenerationId = null);

public sealed record DocumentProcessingNotification(
    Guid DocumentId,
    Guid KnowledgeBaseId,
    Guid UserId,
    string FileName,
    DocumentStatus Status,
    int Progress,
    string Stage,
    int ChunkCount,
    string? Error,
    DateTimeOffset UpdatedAt);
