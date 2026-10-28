using KnowledgeRag.Infrastructure.Persistence;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Application.Security;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.Search;

public sealed class Bm25SearchService(AppDbContext db, IOptions<RagOptions> options) : ILexicalSearchService
{
    private readonly RagOptions _options = options.Value;

    public async Task<IReadOnlyList<HybridCandidate>> SearchAsync(
        Guid knowledgeBaseId,
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var queryTokens = SearchTextTokenizer.Tokenize(query).Distinct(StringComparer.Ordinal).ToArray();
        if (queryTokens.Length == 0) return [];

        var raw = await (
            from c in db.DocumentChunks.AsNoTracking()
            join d in db.Documents.AsNoTracking() on c.DocumentId equals d.Id
            where c.KnowledgeBaseId == knowledgeBaseId &&
                  (d.Status == DocumentStatus.Ready || d.Status == DocumentStatus.Reindexing)
            orderby c.CreatedAt descending
            select new
            {
                c.Id,
                c.DocumentId,
                d.FileName,
                c.ChunkIndex,
                c.Text
            })
            .Take(Math.Max(1_000, _options.Bm25MaxCorpusChunks))
            .ToListAsync(cancellationToken);

        if (raw.Count == 0) return [];

        var docs = raw.Select(x => new TokenizedChunk(
            x.Id,
            x.DocumentId,
            x.FileName,
            x.ChunkIndex,
            x.Text,
            SearchTextTokenizer.Tokenize(x.Text))).ToList();

        var averageLength = docs.Average(x => Math.Max(1, x.Tokens.Count));
        var documentFrequency = queryTokens.ToDictionary(
            token => token,
            token => docs.Count(d => d.Tokens.Contains(token, StringComparer.Ordinal)),
            StringComparer.Ordinal);

        const double k1 = 1.2;
        const double b = 0.75;
        var scored = new List<(TokenizedChunk Chunk, double Score)>();

        foreach (var doc in docs)
        {
            var tf = doc.Tokens
                .Where(t => queryTokens.Contains(t, StringComparer.Ordinal))
                .GroupBy(t => t, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

            double score = 0;
            foreach (var token in queryTokens)
            {
                if (!tf.TryGetValue(token, out var frequency)) continue;
                var df = documentFrequency[token];
                var idf = Math.Log(1 + (docs.Count - df + 0.5) / (df + 0.5));
                var lengthNorm = frequency + k1 * (1 - b + b * doc.Tokens.Count / averageLength);
                score += idf * (frequency * (k1 + 1)) / lengthNorm;
            }

            if (score > 0) scored.Add((doc, score));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .Take(Math.Max(1, limit))
            .Select(x => new HybridCandidate(
                x.Chunk.Id,
                x.Chunk.DocumentId,
                x.Chunk.FileName,
                x.Chunk.ChunkIndex,
                x.Chunk.Text,
                $"/api/knowledge-bases/{knowledgeBaseId}/documents/{x.Chunk.DocumentId}/file",
                null,
                x.Score,
                0))
            .ToList();
    }

    private sealed record TokenizedChunk(
        Guid Id,
        Guid DocumentId,
        string FileName,
        int ChunkIndex,
        string Text,
        IReadOnlyList<string> Tokens);
}
