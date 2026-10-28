namespace KnowledgeRag.Infrastructure.Configuration;

public sealed class RagOptions
{
    public const string SectionName = "Rag";

    public int ChunkSize { get; set; } = 1200;
    public int ChunkOverlap { get; set; } = 180;
    public int TopK { get; set; } = 5;
    public int VectorCandidateCount { get; set; } = 30;
    public int Bm25CandidateCount { get; set; } = 30;
    public int FusionCandidateCount { get; set; } = 24;
    public int RrfK { get; set; } = 60;
    public int Bm25MaxCorpusChunks { get; set; } = 50_000;
    public int EmbeddingBatchSize { get; set; } = 16;
    public int UpsertBatchSize { get; set; } = 32;
    public int MaxUploadMegabytes { get; set; } = 30;
    public int MaxEvidenceTokens { get; set; } = 6000;
    public int MaxEvidenceTokensPerDocument { get; set; } = 2200;

    // Agent workflow controls.
    public bool EnableAgentWorkflow { get; set; } = true;
    public bool EnablePlanner { get; set; } = true;
    public int PlannerMaxQueries { get; set; } = 3;
    public int MultiQueryTopK { get; set; } = 6;
    public int MaxMergedEvidence { get; set; } = 10;
    public bool EnableReviewer { get; set; } = true;
    public int ReviewerMaxRetries { get; set; } = 1;
    public double ReviewerMinConfidence { get; set; } = 0.65;
}
