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

public sealed record RagProgressEvent(
    string RunId,
    long Sequence,
    string StepId,
    string Stage,
    string Type,
    string Actor,
    string Label,
    string Status,
    long? ElapsedMs = null,
    object? Data = null);

public sealed class RagProgressReporter(Func<RagProgressEvent, CancellationToken, Task>? sink)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _sequence;

    public string RunId { get; } = Guid.NewGuid().ToString("N");

    public async Task ReportAsync(
        string stepId,
        string stage,
        string type,
        string actor,
        string label,
        string status,
        CancellationToken cancellationToken,
        long? elapsedMs = null,
        object? data = null)
    {
        if (sink is null) return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var progress = new RagProgressEvent(
                RunId,
                Interlocked.Increment(ref _sequence),
                stepId,
                stage,
                type,
                actor,
                label,
                status,
                elapsedMs,
                data);
            await sink(progress, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed record RagWorkflowInput(
    Guid KnowledgeBaseId,
    string Message,
    string? SessionJson,
    Func<string, CancellationToken, Task>? OnDelta = null,
    RagProgressReporter? Progress = null);

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
