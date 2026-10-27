using KnowledgeRag.Application.Contracts;

namespace KnowledgeRag.Application.Abstractions;

public interface IVectorStore
{
    Task EnsureReadyAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(IReadOnlyList<VectorPoint> points, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VectorCandidate>> SearchAsync(float[] vector, Guid knowledgeBaseId, int topK, CancellationToken cancellationToken = default);
    Task DeleteAsync(IReadOnlyList<Guid> pointIds, CancellationToken cancellationToken = default);
}

public interface ILexicalSearchService
{
    Task<IReadOnlyList<HybridCandidate>> SearchAsync(Guid knowledgeBaseId, string query, int limit, CancellationToken cancellationToken = default);
}

public sealed record VectorPoint(Guid Id, Guid KnowledgeBaseId, Guid DocumentId, string FileName, int ChunkIndex, float[] Vector);
