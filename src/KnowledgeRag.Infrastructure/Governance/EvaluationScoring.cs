using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Conversations;

namespace KnowledgeRag.Infrastructure.Governance;

public sealed record EvaluationCaseAssessment(
    Guid CaseId,
    string Category,
    string Query,
    string? ExpectedAnswerContains,
    bool RequiresKnowledge,
    string Answer,
    IReadOnlyList<SearchHit> CitedSources,
    int RetrievedSourceCount,
    int CitationCount,
    int InvalidCitationCount,
    bool HadCitationMarkers,
    bool IsNoAnswerCase,
    bool InsufficientEvidence,
    bool NonEmpty,
    bool ExpectedPass,
    bool CitationPass,
    bool GroundingPass,
    bool NoAnswerPass,
    bool Passed);

/// <summary>
/// Deterministic Evaluation v2 scoring. It deliberately evaluates the same citation contract used
/// by user-facing Chat instead of treating any retrieved source as proof that the answer is grounded.
/// </summary>
public static class EvaluationScoring
{
    private static readonly string[] InsufficientEvidenceSignals =
    [
        "证据不足",
        "没有找到足够信息",
        "没有足够信息",
        "知识库中没有找到足够信息",
        "无法可靠回答",
        "无法根据现有证据",
        "无法从提供的证据",
        "insufficient evidence",
        "not enough evidence",
        "not enough information",
        "could not find enough information",
        "cannot reliably answer",
        "can't reliably answer",
        "unable to answer from the available evidence",
        "not supported by the available evidence"
    ];

    public static EvaluationCaseAssessment Evaluate(
        EvaluationCaseEntity item,
        string? rawAnswer,
        IReadOnlyList<SearchHit> evidence)
    {
        var citations = CitationContract.Normalize(rawAnswer, evidence);
        var answer = citations.Answer;
        var nonEmpty = !string.IsNullOrWhiteSpace(answer);
        var noAnswerCase = string.Equals(item.Category, "no-answer", StringComparison.OrdinalIgnoreCase);
        var insufficient = IsInsufficientEvidence(answer);
        var expectedPass = string.IsNullOrWhiteSpace(item.ExpectedAnswerContains)
                           || answer.Contains(item.ExpectedAnswerContains, StringComparison.OrdinalIgnoreCase);

        // A knowledge-backed factual answer is grounded only when at least one valid citation
        // resolves to the evidence set used for that answer. Merely retrieving evidence is not enough.
        // A no-answer case is different: the desired behavior is a clean abstention without fabricated refs.
        var citationPass = citations.InvalidCitationCount == 0 &&
                           (noAnswerCase
                               ? true
                               : !item.RequiresKnowledge || (citations.CitationCount > 0 && citations.Sources.Count > 0));
        var groundingPass = noAnswerCase
            ? insufficient && citations.InvalidCitationCount == 0
            : !item.RequiresKnowledge || citationPass;
        var noAnswerPass = !noAnswerCase || insufficient;
        var passed = nonEmpty && expectedPass && citationPass && groundingPass && noAnswerPass;

        return new EvaluationCaseAssessment(
            item.Id,
            item.Category,
            item.Query,
            item.ExpectedAnswerContains,
            item.RequiresKnowledge,
            answer,
            citations.Sources,
            evidence.Count,
            citations.CitationCount,
            citations.InvalidCitationCount,
            citations.HadCitationMarkers,
            noAnswerCase,
            insufficient,
            nonEmpty,
            expectedPass,
            citationPass,
            groundingPass,
            noAnswerPass,
            passed);
    }

    public static bool IsInsufficientEvidence(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return false;
        return InsufficientEvidenceSignals.Any(signal => answer.Contains(signal, StringComparison.OrdinalIgnoreCase));
    }
}
