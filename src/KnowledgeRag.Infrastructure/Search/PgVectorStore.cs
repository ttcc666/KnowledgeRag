using System.Globalization;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Application.Security;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KnowledgeRag.Infrastructure.Search;

public sealed class PgVectorStore : IVectorStore
{
    private readonly DatabaseOptions _database;
    private readonly AiOptions _ai;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public PgVectorStore(IOptions<DatabaseOptions> database, IOptions<AiOptions> ai)
    {
        _database = database.Value;
        _ai = ai.Value;
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;
        RequirePostgres();
        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            var dimensions = Math.Clamp(_ai.EmbeddingDimensions, 1, 16000);
            await using var connection = new NpgsqlConnection(_database.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using (var extension = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector", connection))
                await extension.ExecuteNonQueryAsync(cancellationToken);
            var ddl = $"""
                CREATE TABLE IF NOT EXISTS rag_chunk_vectors (
                    chunk_id uuid PRIMARY KEY,
                    knowledge_base_id uuid NOT NULL,
                    document_id uuid NOT NULL,
                    embedding vector({dimensions}) NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_rag_chunk_vectors_kb ON rag_chunk_vectors (knowledge_base_id);
                CREATE INDEX IF NOT EXISTS ix_rag_chunk_vectors_embedding_hnsw
                    ON rag_chunk_vectors USING hnsw (embedding vector_cosine_ops);
                """;
            await using var command = new NpgsqlCommand(ddl, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally { _initLock.Release(); }
    }

    public async Task UpsertAsync(IReadOnlyList<VectorPoint> points, CancellationToken cancellationToken = default)
    {
        if (points.Count == 0) return;
        await EnsureReadyAsync(cancellationToken);
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var point in points)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO rag_chunk_vectors (chunk_id, knowledge_base_id, document_id, embedding)
                VALUES (@chunk, @kb, @doc, CAST(@embedding AS vector))
                ON CONFLICT (chunk_id) DO UPDATE SET
                    knowledge_base_id = EXCLUDED.knowledge_base_id,
                    document_id = EXCLUDED.document_id,
                    embedding = EXCLUDED.embedding
                """, connection, transaction);
            command.Parameters.AddWithValue("chunk", point.Id);
            command.Parameters.AddWithValue("kb", point.KnowledgeBaseId);
            command.Parameters.AddWithValue("doc", point.DocumentId);
            command.Parameters.AddWithValue("embedding", ToVectorLiteral(point.Vector));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VectorCandidate>> SearchAsync(float[] vector, Guid knowledgeBaseId, int topK, CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        var rows = new List<VectorCandidate>();
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT chunk_id, 1 - (embedding <=> CAST(@embedding AS vector)) AS score
            FROM rag_chunk_vectors
            WHERE knowledge_base_id = @kb
            ORDER BY embedding <=> CAST(@embedding AS vector)
            LIMIT @limit
            """, connection);
        command.Parameters.AddWithValue("embedding", ToVectorLiteral(vector));
        command.Parameters.AddWithValue("kb", knowledgeBaseId);
        command.Parameters.AddWithValue("limit", Math.Clamp(topK, 1, 1000));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new VectorCandidate(reader.GetGuid(0), reader.IsDBNull(1) ? 0 : reader.GetDouble(1)));
        return rows;
    }

    public async Task DeleteAsync(IReadOnlyList<Guid> pointIds, CancellationToken cancellationToken = default)
    {
        if (pointIds.Count == 0) return;
        await EnsureReadyAsync(cancellationToken);
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("DELETE FROM rag_chunk_vectors WHERE chunk_id = ANY(@ids)", connection);
        command.Parameters.AddWithValue("ids", pointIds.ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void RequirePostgres()
    {
        if (!_database.Provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase) && !_database.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SearchBackend:Vector=pgvector 仅能在 Database:Provider=PostgreSql 时使用。");
    }

    private static string ToVectorLiteral(IEnumerable<float> values)
        => "[" + string.Join(',', values.Select(x => x.ToString("R", CultureInfo.InvariantCulture))) + "]";
}
