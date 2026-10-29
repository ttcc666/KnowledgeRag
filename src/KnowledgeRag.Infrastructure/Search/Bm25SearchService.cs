using KnowledgeRag.Infrastructure.Persistence;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.Search;

public sealed class Bm25SearchService(AppDbContext db, IOptions<RagOptions> options) : ILexicalSearchService
{
    private readonly RagOptions _options = options.Value;

    public Task<IReadOnlyList<HybridCandidate>> SearchAsync(Guid knowledgeBaseId, string query, int limit, CancellationToken cancellationToken = default)
        => SearchAsync(knowledgeBaseId, Guid.Empty, query, limit, cancellationToken);

    public async Task<IReadOnlyList<HybridCandidate>> SearchAsync(
        Guid knowledgeBaseId,
        Guid indexGenerationId,
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var queryTokens = SearchTextTokenizer.Tokenize(query).Distinct(StringComparer.Ordinal).ToArray();
        if (queryTokens.Length == 0) return [];

        var raw = await LoadCorpusAsync(
            knowledgeBaseId,
            indexGenerationId,
            Math.Max(1_000, _options.Bm25MaxCorpusChunks),
            cancellationToken);

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

    private async Task<IReadOnlyList<CorpusChunk>> LoadCorpusAsync(
        Guid knowledgeBaseId,
        Guid indexGenerationId,
        int maxCorpusChunks,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsSqlite())
        {
            return await (
                from c in db.DocumentChunks.AsNoTracking()
                join d in db.Documents.AsNoTracking() on c.DocumentId equals d.Id
                where c.KnowledgeBaseId == knowledgeBaseId && c.IndexGenerationId == indexGenerationId && d.ChunkCount > 0
                orderby c.CreatedAt descending, c.Id descending
                select new CorpusChunk(
                    c.Id,
                    c.DocumentId,
                    d.FileName,
                    c.ChunkIndex,
                    c.Text))
                .Take(maxCorpusChunks)
                .ToListAsync(cancellationToken);
        }

        // EF Core's SQLite provider can read/write DateTimeOffset but cannot translate
        // ordering/comparison over it. Keep the corpus bounded in SQLite itself instead
        // of materializing every chunk just to sort on the client. julianday() correctly
        // normalizes ISO-8601 offsets before ordering.
        var chunks = await db.DocumentChunks
            .FromSqlInterpolated($"""
                SELECT c.*
                FROM "DocumentChunks" AS c
                INNER JOIN "Documents" AS d ON c."DocumentId" = d."Id"
                WHERE c."KnowledgeBaseId" = {knowledgeBaseId}
                  AND c."IndexGenerationId" = {indexGenerationId}
                  AND d."ChunkCount" > 0
                ORDER BY julianday(c."CreatedAt") DESC, c."Id" DESC
                LIMIT {maxCorpusChunks}
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (chunks.Count == 0) return [];

        var documentIds = chunks.Select(x => x.DocumentId).Distinct().ToArray();
        var fileNames = await db.Documents
            .AsNoTracking()
            .Where(x => documentIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FileName })
            .ToDictionaryAsync(x => x.Id, x => x.FileName, cancellationToken);

        return chunks
            .Where(x => fileNames.ContainsKey(x.DocumentId))
            .Select(x => new CorpusChunk(
                x.Id,
                x.DocumentId,
                fileNames[x.DocumentId],
                x.ChunkIndex,
                x.Text))
            .ToList();
    }

    private sealed record CorpusChunk(
        Guid Id,
        Guid DocumentId,
        string FileName,
        int ChunkIndex,
        string Text);

    private sealed record TokenizedChunk(
        Guid Id,
        Guid DocumentId,
        string FileName,
        int ChunkIndex,
        string Text,
        IReadOnlyList<string> Tokens);
}
