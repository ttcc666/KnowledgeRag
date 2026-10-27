using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Infrastructure.Configuration;
using KnowledgeRag.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.Search;

/// <summary>
/// Executes query variants in isolated DI scopes so each search owns its own DbContext,
/// merges the ranked lists deterministically, and performs one final rerank against the
/// user's original question.
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
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("rag.multi_query_search");
        var normalized = NormalizeQueries(originalQuery, queries, _options.PlannerMaxQueries);
        activity?.SetTag("rag.query_count", normalized.Count);

        var tasks = normalized.Select(query => SearchOneAsync(knowledgeBaseId, query, cancellationToken)).ToArray();
        var rankedLists = await Task.WhenAll(tasks);
        var merged = Merge(rankedLists, Math.Max(_options.MaxMergedEvidence, _options.TopK));
        activity?.SetTag("rag.merged_candidates", merged.Count);
        if (merged.Count == 0) return [];

        var finalTopK = Math.Clamp(_options.TopK, 1, 20);
        var reranked = await reranker.RerankAsync(originalQuery, merged, finalTopK, cancellationToken);
        var result = reranked.Select(x => new SearchHit(
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
            x.PageNumber)).ToList();

        activity?.SetTag("rag.result_count", result.Count);
        return result;
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

    private async Task<IReadOnlyList<SearchHit>> SearchOneAsync(Guid knowledgeBaseId, string query, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var search = scope.ServiceProvider.GetRequiredService<KnowledgeSearchService>();
        return await search.SearchAsync(knowledgeBaseId, query, _options.MultiQueryTopK, cancellationToken);
    }

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
}
