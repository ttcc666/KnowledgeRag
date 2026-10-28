using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Application.Security;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.AI;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.Search;

public sealed class RerankerService(
    OpenAiCompatibleRerankerService llm,
    IOptions<RerankerOptions> options)
{
    private readonly RerankerOptions _options = options.Value;

    public async Task<IReadOnlyList<HybridCandidate>> RerankAsync(
        string query,
        IReadOnlyList<HybridCandidate> candidates,
        int topK,
        CancellationToken cancellationToken = default,
        ResolvedAiEndpoint? endpoint = null)
    {
        if (candidates.Count == 0) return [];
        var take = Math.Min(candidates.Count, Math.Max(topK, _options.CandidateCount));
        var working = candidates.Take(take).ToList();
        var mode = (_options.Mode ?? "local").Trim().ToLowerInvariant();

        if (mode == "none") return working.Take(topK).ToList();

        IReadOnlyDictionary<Guid, double> llmScores = new Dictionary<Guid, double>();
        if (mode == "llm")
            llmScores = await llm.RerankAsync(query, working, endpoint, cancellationToken);

        var localScores = ComputeLocalScores(query, working);
        var reranked = working
            .Select(x =>
            {
                var score = llmScores.TryGetValue(x.PointId, out var llmScore)
                    ? 0.8 * llmScore + 0.2 * localScores[x.PointId]
                    : localScores[x.PointId];
                return x with { RerankScore = score };
            })
            .OrderByDescending(x => x.RerankScore)
            .ThenByDescending(x => x.FusionScore)
            .Take(topK)
            .ToList();

        return reranked;
    }

    private static IReadOnlyDictionary<Guid, double> ComputeLocalScores(string query, IReadOnlyList<HybridCandidate> candidates)
    {
        var queryTokens = SearchTextTokenizer.Tokenize(query).ToHashSet(StringComparer.Ordinal);
        var maxFusion = Math.Max(1e-9, candidates.Max(x => x.FusionScore));
        var result = new Dictionary<Guid, double>();

        foreach (var candidate in candidates)
        {
            var docTokens = SearchTextTokenizer.Tokenize(candidate.Text).ToHashSet(StringComparer.Ordinal);
            var overlap = queryTokens.Count == 0 ? 0 : queryTokens.Count(docTokens.Contains) / (double)queryTokens.Count;
            var phraseBonus = !string.IsNullOrWhiteSpace(query) && candidate.Text.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0;
            var normalizedFusion = candidate.FusionScore / maxFusion;
            result[candidate.PointId] = Math.Clamp(0.55 * normalizedFusion + 0.35 * overlap + 0.10 * phraseBonus, 0, 1);
        }

        return result;
    }
}
