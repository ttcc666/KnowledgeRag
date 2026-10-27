using KnowledgeRag.Application.Contracts;

namespace KnowledgeRag.Infrastructure.Conversations;

public sealed class QueryPlan
{
    public string Intent { get; set; } = "answer";
    public bool RequiresKnowledge { get; set; } = true;
    public List<string> Queries { get; set; } = [];
    public string AnswerMode { get; set; } = "grounded-answer";
}

public sealed class EvidenceReview
{
    public bool Supported { get; set; }
    public double Confidence { get; set; }
    public List<string> UnsupportedClaims { get; set; } = [];
    public List<string> MissingEvidence { get; set; } = [];
    public List<string> CitationErrors { get; set; } = [];
    public List<string> SupplementalQueries { get; set; } = [];
}

public sealed record RagWorkflowInput(
    Guid KnowledgeBaseId,
    string Message,
    string? SessionJson,
    Func<string, CancellationToken, Task>? OnDelta = null);

public sealed record RagWorkflowState(
    RagWorkflowInput Input,
    QueryPlan? Plan = null,
    IReadOnlyList<SearchHit>? Evidence = null,
    string Answer = "",
    string SessionJson = "",
    EvidenceReview? Review = null,
    int RetrievalRound = 0,
    bool ReviewPassed = false)
{
    public IReadOnlyList<SearchHit> SafeEvidence => Evidence ?? [];
}
