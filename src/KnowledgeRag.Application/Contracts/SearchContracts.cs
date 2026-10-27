namespace KnowledgeRag.Application.Contracts;

public sealed record SearchRequest(Guid KnowledgeBaseId, string Query, int? TopK = null);

public sealed record SearchHit(
    Guid PointId,
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    string Text,
    double Score,
    string SourceLink,
    double? VectorScore = null,
    double? Bm25Score = null,
    double? FusionScore = null,
    double? RerankScore = null,
    int? PageNumber = null);

public sealed record VectorCandidate(Guid PointId, double Score);

public sealed record HybridCandidate(
    Guid PointId,
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    string Text,
    string SourceLink,
    double? VectorScore,
    double? Bm25Score,
    double FusionScore,
    double RerankScore = 0,
    int? PageNumber = null);
