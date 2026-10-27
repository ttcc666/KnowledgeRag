using KnowledgeRag.Domain.Entities;

namespace KnowledgeRag.Application.Contracts;

public sealed record DailyActivityPoint(string Date, int Documents, int Conversations);

public sealed record IngestionJobSummary(
    Guid DocumentId,
    Guid KnowledgeBaseId,
    string KnowledgeBaseName,
    string FileName,
    DocumentStatus Status,
    int ChunkCount,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    double ElapsedSeconds);

public sealed record RetrievalMetricsSummary(
    long QueryCount,
    double P50LatencyMs,
    double P95LatencyMs,
    double NoResultRate,
    double AverageTopScore,
    long EstimatedInputTokens,
    long EstimatedOutputTokens,
    long ChatTurns);

public sealed record DashboardSummary(
    int KnowledgeBaseCount,
    int ReadyDocumentCount,
    int ProcessingDocumentCount,
    int FailedDocumentCount,
    int CancelledDocumentCount,
    int TotalChunkCount,
    int ConversationCount,
    IReadOnlyList<DailyActivityPoint> Activity,
    IReadOnlyList<IngestionJobSummary> RecentIngestion,
    RetrievalMetricsSummary Retrieval);

public sealed record ProductSettingsSummary(
    string DatabaseProvider,
    string ChatModel,
    string EmbeddingModel,
    string VectorBackend,
    string LexicalBackend,
    string RerankerMode,
    string StorageProvider,
    string CapTransport,
    int CapFailedRetryCount,
    int ChunkSize,
    int ChunkOverlap,
    int TopK,
    int MaxUploadMegabytes,
    bool OpenTelemetryEnabled);
