using System.Diagnostics;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Infrastructure.Configuration;
using KnowledgeRag.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.Search;

public sealed record QuerySearchProgress(
    int Index,
    int QueryCount,
    string Query,
    string Status,
    int ResultCount = 0,
    long? ElapsedMs = null);

/// <summary>
/// Executes query variants in isolated DI scopes so each search owns its own DbContext,
/// merges the ranked lists deterministically, and performs one final rerank against the
/// user's original question. The original query is mandatory; planner variants are best-effort.
/// </summary>
public sealed class MultiQuerySearchService(
    IServiceScopeFactory scopeFactory,
    RerankerService reranker,
    IOptions<RagOptions> options)
{
    private readonly RagOptions _options = options.Value;

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        Guid knowledgeBaseId,
        string originalQuery,
        IReadOnlyList<string> queries,
        CancellationToken cancellationToken = default,
        Func<QuerySearchProgress, CancellationToken, Task>? onQueryProgress = null,
        int? maxQueries = null,
        int? queryTopK = null,
        int? maxMergedEvidence = null,
        int? finalTopK = null,
        ResolvedAiEndpoint? rerankerEndpoint = null)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("rag.multi_query_search");
        var normalized = NormalizeQueries(originalQuery, queries, maxQueries ?? _options.PlannerMaxQueries);
        var perQueryTopK = Math.Clamp(queryTopK ?? _options.MultiQueryTopK, 1, 30);
        var mergedLimit = Math.Clamp(maxMergedEvidence ?? _options.MaxMergedEvidence, 1, 40);
        var outputTopK = Math.Clamp(finalTopK ?? _options.TopK, 1, 20);
        activity?.SetTag("rag.query_count", normalized.Count);
        activity?.SetTag("rag.query_top_k", perQueryTopK);
        activity?.SetTag("rag.max_merged_evidence", mergedLimit);
        activity?.SetTag("rag.final_top_k", outputTopK);

        var tasks = normalized.Select((query, index) =>
            SearchOneWithProgressAsync(knowledgeBaseId, query, index + 1, normalized.Count, perQueryTopK, onQueryProgress, cancellationToken)).ToArray();
        var queryResults = await Task.WhenAll(tasks);
        var original = queryResults[0];
        if (!original.Succeeded)
            throw new InvalidOperationException("原始检索查询失败，无法安全继续 RAG Workflow。", original.Error);

        var rankedLists = queryResults.Where(x => x.Succeeded).Select(x => x.Results).ToArray();
        activity?.SetTag("rag.query_failed_count", queryResults.Count(x => !x.Succeeded));
        var merged = Merge(rankedLists, Math.Max(mergedLimit, outputTopK));
        activity?.SetTag("rag.merged_candidates", merged.Count);
        if (merged.Count == 0) return [];

        var reranked = await reranker.RerankAsync(originalQuery, merged, outputTopK, cancellationToken, rerankerEndpoint);
        var result = reranked.Select(ToSearchHit).ToList();
        var budgeted = ApplyEvidenceBudget(result);
        activity?.SetTag("rag.result_count", budgeted.Count);
        activity?.SetTag("rag.evidence_token_estimate", budgeted.Sum(x => EstimateTokens(x.Text)));
        return budgeted;
    }

    /// <summary>
    /// Re-ranks evidence collected across multiple retrieval rounds as one candidate set so
    /// round-local normalized scores are never directly compared during Reviewer repair.
    /// </summary>
    public async Task<IReadOnlyList<SearchHit>> RerankEvidenceAsync(
        string originalQuery,
        IReadOnlyList<SearchHit> evidence,
        int topK,
        CancellationToken cancellationToken = default,
        ResolvedAiEndpoint? rerankerEndpoint = null)
    {
        if (evidence.Count == 0) return [];
        var candidates = evidence
            .OrderByDescending(x => x.FusionScore ?? x.Score)
            .ThenBy(x => x.PointId)
            .Select(x => new HybridCandidate(
                x.PointId,
                x.DocumentId,
                x.FileName,
                x.ChunkIndex,
                x.Text,
                x.SourceLink,
                x.VectorScore,
                x.Bm25Score,
                x.FusionScore ?? x.Score,
                x.RerankScore ?? 0,
                x.PageNumber))
            .ToList();
        var reranked = await reranker.RerankAsync(originalQuery, candidates, Math.Clamp(topK, 1, 40), cancellationToken, rerankerEndpoint);
        return ApplyEvidenceBudget(reranked.Select(ToSearchHit).ToList());
    }

    public static IReadOnlyList<string> NormalizeQueries(string originalQuery, IEnumerable<string>? queries, int maxQueries)
    {
        var limit = Math.Clamp(maxQueries, 1, 8);
        var original = originalQuery.Trim();
        var result = new List<string>(limit);
        if (!string.IsNullOrWhiteSpace(original)) result.Add(original);

        foreach (var variant in queries ?? [])
        {
            var query = variant?.Trim();
            if (string.IsNullOrWhiteSpace(query) || result.Contains(query, StringComparer.OrdinalIgnoreCase)) continue;
            if (result.Count >= limit) break;
            result.Add(query);
        }

        return result;
    }

    private async Task<QuerySearchResult> SearchOneWithProgressAsync(
        Guid knowledgeBaseId,
        string query,
        int index,
        int queryCount,
        int topK,
        Func<QuerySearchProgress, CancellationToken, Task>? onProgress,
        CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        if (onProgress is not null)
            await onProgress(new QuerySearchProgress(index, queryCount, query, "running"), cancellationToken);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var search = scope.ServiceProvider.GetRequiredService<KnowledgeSearchService>();
            var results = await search.SearchAsync(knowledgeBaseId, query, topK, cancellationToken);
            if (onProgress is not null)
                await onProgress(new QuerySearchProgress(index, queryCount, query, "completed", results.Count, timer.ElapsedMilliseconds), cancellationToken);
            return new QuerySearchResult(true, results, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (onProgress is not null)
                await onProgress(new QuerySearchProgress(index, queryCount, query, "failed", 0, timer.ElapsedMilliseconds), cancellationToken);
            return new QuerySearchResult(false, [], ex);
        }
    }

    private IReadOnlyList<SearchHit> ApplyEvidenceBudget(IReadOnlyList<SearchHit> ranked)
    {
        var totalBudget = Math.Clamp(_options.MaxEvidenceTokens, 256, 100_000);
        var perDocumentBudget = Math.Clamp(_options.MaxEvidenceTokensPerDocument, 128, totalBudget);
        var remainingTotal = totalBudget;
        var perDocumentUsed = new Dictionary<Guid, int>();
        var result = new List<SearchHit>(ranked.Count);

        foreach (var hit in ranked)
        {
            if (remainingTotal <= 0) break;
            var used = perDocumentUsed.GetValueOrDefault(hit.DocumentId);
            var documentRemaining = perDocumentBudget - used;
            if (documentRemaining <= 0) continue;

            var allowedTokens = Math.Min(remainingTotal, documentRemaining);
            var tokenEstimate = EstimateTokens(hit.Text);
            var text = hit.Text;
            var consumed = tokenEstimate;
            if (tokenEstimate > allowedTokens)
            {
                var maxChars = Math.Max(1, (int)Math.Floor(allowedTokens * 3.5));
                text = hit.Text.Length <= maxChars ? hit.Text : hit.Text[..maxChars].TrimEnd() + "…";
                consumed = Math.Min(allowedTokens, EstimateTokens(text));
            }

            if (consumed <= 0) continue;
            result.Add(hit with { Text = text });
            remainingTotal -= consumed;
            perDocumentUsed[hit.DocumentId] = used + consumed;
        }

        return result;
    }

    private static int EstimateTokens(string? text)
        => string.IsNullOrEmpty(text) ? 0 : Math.Max(1, (int)Math.Ceiling(text.Length / 3.5));

    private static SearchHit ToSearchHit(HybridCandidate x)
        => new(
            x.PointId,
            x.DocumentId,
            x.FileName,
            x.ChunkIndex,
            x.Text,
            x.RerankScore > 0 ? x.RerankScore : x.FusionScore,
            x.SourceLink,
            x.VectorScore,
            x.Bm25Score,
            x.FusionScore,
            x.RerankScore,
            x.PageNumber);

    private static List<HybridCandidate> Merge(IReadOnlyList<SearchHit>[] rankedLists, int maxCandidates)
    {
        const double queryRrfK = 60;
        var combined = new Dictionary<Guid, (SearchHit Hit, double QueryRrf)>();

        foreach (var list in rankedLists)
        {
            for (var rank = 0; rank < list.Count; rank++)
            {
                var hit = list[rank];
                var contribution = 1d / (queryRrfK + rank + 1);
                if (combined.TryGetValue(hit.PointId, out var existing))
                {
                    var preferred = hit.Score > existing.Hit.Score ? hit : existing.Hit;
                    combined[hit.PointId] = (preferred, existing.QueryRrf + contribution);
                }
                else
                {
                    combined[hit.PointId] = (hit, contribution);
                }
            }
        }

        return combined.Values
            .OrderByDescending(x => x.QueryRrf)
            .ThenByDescending(x => x.Hit.Score)
            .Take(Math.Clamp(maxCandidates, 1, 40))
            .Select(x => new HybridCandidate(
                x.Hit.PointId,
                x.Hit.DocumentId,
                x.Hit.FileName,
                x.Hit.ChunkIndex,
                x.Hit.Text,
                x.Hit.SourceLink,
                x.Hit.VectorScore,
                x.Hit.Bm25Score,
                x.QueryRrf + (x.Hit.FusionScore ?? 0),
                x.Hit.RerankScore ?? 0,
                x.Hit.PageNumber))
            .ToList();
    }

    private sealed record QuerySearchResult(bool Succeeded, IReadOnlyList<SearchHit> Results, Exception? Error);
}
