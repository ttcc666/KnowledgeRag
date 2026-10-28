using System.Diagnostics;
using System.Text.RegularExpressions;
using KnowledgeRag.Infrastructure.Persistence;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Configuration;
using KnowledgeRag.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.Search;

public sealed class KnowledgeSearchService(
    AppDbContext db,
    OpenAiCompatibleEmbeddingService embeddings,
    IVectorStore vectorStore,
    ILexicalSearchService lexical,
    RerankerService reranker,
    ProductMetricsCollector metrics,
    IOptions<RagOptions> options,
    IOptions<SearchBackendOptions> backendOptions)
{
    private readonly RagOptions _options = options.Value;
    private readonly SearchBackendOptions _backend = backendOptions.Value;
    private static readonly Regex PageMarker = new(@"^\[\[PAGE:(\d+)\]\]\s*", RegexOptions.Compiled);

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(Guid knowledgeBaseId, string query, int? topK = null, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("rag.search");
        activity?.SetTag("rag.knowledge_base_id", knowledgeBaseId.ToString());
        activity?.SetTag("rag.vector_backend", _backend.Vector);
        activity?.SetTag("rag.lexical_backend", _backend.Lexical);
        if (string.IsNullOrWhiteSpace(query)) return [];
        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<SearchHit> result = [];
        try
        {
            var finalTopK = Math.Clamp(topK ?? _options.TopK, 1, 20);
            activity?.SetTag("rag.top_k", finalTopK);
            var vector = await embeddings.GenerateAsync(query, cancellationToken);
            var vectorTask = vectorStore.SearchAsync(vector, knowledgeBaseId, Math.Max(finalTopK, _options.VectorCandidateCount), cancellationToken);
            var lexicalTask = lexical.SearchAsync(knowledgeBaseId, query, Math.Max(finalTopK, _options.Bm25CandidateCount), cancellationToken);
            await Task.WhenAll(vectorTask, lexicalTask);
            var vectorHits = await vectorTask;
            var lexicalHits = await lexicalTask;
            activity?.SetTag("rag.vector_candidates", vectorHits.Count);
            activity?.SetTag("rag.lexical_candidates", lexicalHits.Count);

            var pointIds = vectorHits.Select(x => x.PointId).Concat(lexicalHits.Select(x => x.PointId)).Distinct().ToArray();
            if (pointIds.Length == 0) return result;
            var dbChunks = await (from c in db.DocumentChunks.AsNoTracking()
                                  join d in db.Documents.AsNoTracking() on c.DocumentId equals d.Id
                                  where c.KnowledgeBaseId == knowledgeBaseId && pointIds.Contains(c.Id) &&
                                        (d.Status == DocumentStatus.Ready || d.Status == DocumentStatus.Reindexing)
                                  select new { c.Id, c.DocumentId, d.FileName, c.ChunkIndex, c.Text }).ToListAsync(cancellationToken);

            var data = dbChunks.ToDictionary(x => x.Id);
            var vectorScores = vectorHits.ToDictionary(x => x.PointId, x => x.Score);
            var lexicalScores = lexicalHits.ToDictionary(x => x.PointId, x => x.Bm25Score ?? 0);
            var fusion = new Dictionary<Guid, double>();
            var rrfK = Math.Max(1, _options.RrfK);
            for (var i = 0; i < vectorHits.Count; i++) fusion[vectorHits[i].PointId] = fusion.GetValueOrDefault(vectorHits[i].PointId) + 1.0 / (rrfK + i + 1);
            for (var i = 0; i < lexicalHits.Count; i++) fusion[lexicalHits[i].PointId] = fusion.GetValueOrDefault(lexicalHits[i].PointId) + 1.0 / (rrfK + i + 1);

            var fused = fusion.Where(x => data.ContainsKey(x.Key)).OrderByDescending(x => x.Value)
                .Take(Math.Max(finalTopK, _options.FusionCandidateCount)).Select(x =>
                {
                    var row = data[x.Key];
                    var (text, pageNumber) = NormalizeChunk(row.Text);
                    return new HybridCandidate(row.Id, row.DocumentId, row.FileName, row.ChunkIndex, text,
                        $"/api/knowledge-bases/{knowledgeBaseId}/documents/{row.DocumentId}/file",
                        vectorScores.TryGetValue(row.Id, out var vs) ? vs : null,
                        lexicalScores.TryGetValue(row.Id, out var ls) ? ls : null, x.Value, PageNumber: pageNumber);
                }).ToList();

            var reranked = await reranker.RerankAsync(query, fused, finalTopK, cancellationToken);
            result = reranked.Select(x => new SearchHit(x.PointId, x.DocumentId, x.FileName, x.ChunkIndex, x.Text,
                x.RerankScore > 0 ? x.RerankScore : x.FusionScore, x.SourceLink, x.VectorScore, x.Bm25Score, x.FusionScore, x.RerankScore, x.PageNumber)).ToList();
            activity?.SetTag("rag.result_count", result.Count);
            return result;
        }
        finally
        {
            stopwatch.Stop();
            var rerankScores = result.Where(x => x.RerankScore.HasValue && x.RerankScore.Value > 0).Select(x => x.RerankScore!.Value).ToArray();
            metrics.RecordSearch(
                stopwatch.Elapsed.TotalMilliseconds,
                result.Count,
                result.Count == 0 ? 0 : result[0].Score,
                rerankScores.Length == 0 ? null : rerankScores.Average());
        }
    }

    private static (string Text, int? PageNumber) NormalizeChunk(string text)
    {
        var match = PageMarker.Match(text);
        if (!match.Success) return (text, null);
        return (PageMarker.Replace(text, string.Empty, 1).TrimStart(), int.TryParse(match.Groups[1].Value, out var page) ? page : null);
    }
}
