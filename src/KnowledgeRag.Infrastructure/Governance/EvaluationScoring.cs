using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Conversations;

namespace KnowledgeRag.Infrastructure.Governance;

public sealed record EvaluationRetrievalAssessment(
    bool Applicable,
    int ExpectedSourceCount,
    int MatchedSourceCount,
    bool Hit,
    double RecallAtK,
    double Mrr,
    double Ndcg,
    bool Passed);

public sealed record EvaluationCaseAssessment(
    Guid CaseId,
    string Category,
    string Query,
    string? ExpectedAnswerContains,
    IReadOnlyList<EvaluationExpectedSource> ExpectedSources,
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
    EvaluationRetrievalAssessment Retrieval,
    bool Passed);

/// <summary>
/// Deterministic Evaluation v3 scoring. Answer/citation checks use the same final citation
/// contract as Chat, while optional expected source labels independently score retrieval.
/// </summary>
public static class EvaluationScoring
{
    private static readonly string[] InsufficientEvidenceSignals =
    [
        "证据不足",
        "没有找到足够信息",
        "没有足够信息",
        "知识库中没有找到足够信息",
        "无法可靠支持完整回答",
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
        var expectation = EvaluationExpectationCodec.Parse(item.ExpectedAnswerContains);
        var citations = CitationContract.Normalize(rawAnswer, evidence);
        var answer = citations.Answer;
        var nonEmpty = !string.IsNullOrWhiteSpace(answer);
        var noAnswerCase = string.Equals(item.Category, "no-answer", StringComparison.OrdinalIgnoreCase);
        var insufficient = IsInsufficientEvidence(answer);
        var expectedPass = string.IsNullOrWhiteSpace(expectation.AnswerContains)
                           || answer.Contains(expectation.AnswerContains, StringComparison.OrdinalIgnoreCase);

        var retrieval = EvaluateRetrieval(expectation.Sources, evidence);

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
        var passed = nonEmpty && expectedPass && citationPass && groundingPass && noAnswerPass && retrieval.Passed;

        return new EvaluationCaseAssessment(
            item.Id,
            item.Category,
            item.Query,
            expectation.AnswerContains,
            expectation.Sources,
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
            retrieval,
            passed);
    }

    public static EvaluationRetrievalAssessment EvaluateRetrieval(
        IReadOnlyList<EvaluationExpectedSource>? expectedSources,
        IReadOnlyList<SearchHit> evidence)
    {
        var expected = (expectedSources ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.FileName))
            .ToList();
        if (expected.Count == 0)
            return new EvaluationRetrievalAssessment(false, 0, 0, false, 1, 1, 1, true);

        var matched = new HashSet<int>();
        var reciprocalRank = 0d;
        var dcg = 0d;
        for (var rank = 0; rank < evidence.Count; rank++)
        {
            var hit = evidence[rank];
            var expectedIndex = FindExpectedIndex(expected, hit, matched);
            if (expectedIndex < 0) continue;
            matched.Add(expectedIndex);
            if (reciprocalRank == 0) reciprocalRank = 1d / (rank + 1);
            dcg += 1d / Math.Log2(rank + 2);
        }

        var recall = matched.Count / (double)expected.Count;
        var idealRelevant = Math.Min(expected.Count, evidence.Count);
        var idcg = 0d;
        for (var i = 0; i < idealRelevant; i++) idcg += 1d / Math.Log2(i + 2);
        var ndcg = idcg <= 0 ? 0 : dcg / idcg;
        var hitRate = matched.Count > 0;
        return new EvaluationRetrievalAssessment(
            true,
            expected.Count,
            matched.Count,
            hitRate,
            recall,
            reciprocalRank,
            ndcg,
            hitRate);
    }

    public static bool IsInsufficientEvidence(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return false;
        return InsufficientEvidenceSignals.Any(signal => answer.Contains(signal, StringComparison.OrdinalIgnoreCase));
    }

    private static int FindExpectedIndex(
        IReadOnlyList<EvaluationExpectedSource> expected,
        SearchHit hit,
        ISet<int> alreadyMatched)
    {
        for (var i = 0; i < expected.Count; i++)
        {
            if (alreadyMatched.Contains(i)) continue;
            var item = expected[i];
            if (!string.Equals(item.FileName, hit.FileName, StringComparison.OrdinalIgnoreCase)) continue;
            if (item.ChunkIndex.HasValue && item.ChunkIndex.Value != hit.ChunkIndex) continue;
            return i;
        }
        return -1;
    }
}
