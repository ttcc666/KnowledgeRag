using KnowledgeRag.Application.Contracts;

namespace KnowledgeRag.Infrastructure.Conversations;

public static class RagWorkflowPolicy
{
    public static QueryPlan NormalizePlan(QueryPlan? plan, string originalQuestion, int maxQueries)
    {
        var normalized = plan ?? new QueryPlan();
        normalized.Intent = string.IsNullOrWhiteSpace(normalized.Intent) ? "answer" : normalized.Intent.Trim();
        normalized.AnswerMode = string.IsNullOrWhiteSpace(normalized.AnswerMode) ? "grounded-answer" : normalized.AnswerMode.Trim();

        var limit = Math.Clamp(maxQueries, 1, 8);
        normalized.Queries = normalized.RequiresKnowledge
            ? BuildQueries(originalQuestion, normalized.Queries, limit)
            : [];

        return normalized;
    }

    public static IReadOnlyList<SearchHit> MergeEvidence(
        IEnumerable<SearchHit>? current,
        IEnumerable<SearchHit>? supplemental,
        int maxEvidence)
    {
        return (current ?? [])
            .Concat(supplemental ?? [])
            .GroupBy(x => x.PointId)
            .Select(group => group.OrderByDescending(x => x.Score).First())
            .OrderByDescending(x => x.Score)
            .Take(Math.Clamp(maxEvidence, 1, 40))
            .ToList();
    }

    public static bool IsReviewAccepted(EvidenceReview? review, double minimumConfidence)
        => review is not null
           && review.Supported
           && review.Confidence >= Math.Clamp(minimumConfidence, 0, 1)
           && review.UnsupportedClaims.Count == 0
           && review.CitationErrors.Count == 0;

    public static IReadOnlyList<string> SupplementalQueries(EvidenceReview? review, string originalQuestion, int maxQueries)
    {
        var candidates = (review?.SupplementalQueries ?? [])
            .Concat(review?.MissingEvidence ?? []);
        return BuildQueries(originalQuestion, candidates, Math.Clamp(maxQueries, 1, 8));
    }

    public static string SafeUnsupportedAnswer()
        => "基于当前知识库证据，我无法可靠支持完整回答。知识库中没有找到足够信息，请补充资料或缩小问题范围后重试。";

    private static List<string> BuildQueries(string originalQuestion, IEnumerable<string>? variants, int limit)
    {
        var original = originalQuestion.Trim();
        var result = new List<string>(limit);
        if (!string.IsNullOrWhiteSpace(original)) result.Add(original);

        foreach (var variant in variants ?? [])
        {
            var query = variant?.Trim();
            if (string.IsNullOrWhiteSpace(query) || result.Contains(query, StringComparer.OrdinalIgnoreCase)) continue;
            if (result.Count >= limit) break;
            result.Add(query);
        }

        return result;
    }
}
