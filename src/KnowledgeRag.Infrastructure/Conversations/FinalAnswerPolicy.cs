using KnowledgeRag.Application.Contracts;

namespace KnowledgeRag.Infrastructure.Conversations;

public sealed record FinalAnswerValidationResult(
    string Answer,
    IReadOnlyList<SearchHit> Sources,
    bool Accepted,
    string Status,
    int CitationCount,
    int InvalidCitationCount,
    bool HadCitationMarkers);

/// <summary>
/// Deterministic final gate that runs after the Agent/Reviewer workflow and before any
/// answer is persisted or released to the client. It never trusts model-generated
/// citation text directly: CitationContract resolves it against the exact Evidence set.
/// </summary>
public static class FinalAnswerPolicy
{
    public static FinalAnswerValidationResult Validate(
        string? rawAnswer,
        IReadOnlyList<SearchHit> evidence,
        bool requiresKnowledge)
    {
        var normalized = CitationContract.Normalize(rawAnswer, evidence);

        if (normalized.InvalidCitationCount > 0)
            return Reject(normalized, "invalid-citation");

        if (requiresKnowledge && !IsInsufficientEvidence(normalized.Answer) &&
            (normalized.CitationCount == 0 || normalized.Sources.Count == 0))
            return Reject(normalized, "missing-citation");

        return new FinalAnswerValidationResult(
            normalized.Answer,
            normalized.Sources,
            true,
            IsInsufficientEvidence(normalized.Answer) ? "abstained" : "accepted",
            normalized.CitationCount,
            normalized.InvalidCitationCount,
            normalized.HadCitationMarkers);
    }

    public static bool IsInsufficientEvidence(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return false;
        var signals = new[]
        {
            "证据不足",
            "没有找到足够信息",
            "没有足够信息",
            "知识库中没有找到足够信息",
            "无法可靠支持完整回答",
            "无法可靠回答",
            "insufficient evidence",
            "not enough evidence",
            "not enough information",
            "cannot reliably answer"
        };
        return signals.Any(signal => answer.Contains(signal, StringComparison.OrdinalIgnoreCase));
    }

    private static FinalAnswerValidationResult Reject(CitationNormalizationResult normalized, string status)
        => new(
            RagWorkflowPolicy.SafeUnsupportedAnswer(),
            [],
            false,
            status,
            normalized.CitationCount,
            normalized.InvalidCitationCount,
            normalized.HadCitationMarkers);
}
