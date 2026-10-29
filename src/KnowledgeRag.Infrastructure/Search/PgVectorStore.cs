using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<Guid, int> _initializedTables = new();

    public PgVectorStore(IOptions<DatabaseOptions> database, IOptions<AiOptions> ai)
    {
        _database = database.Value;
        _ai = ai.Value;
    }

    public Task EnsureReadyAsync(CancellationToken cancellationToken = default)
        => EnsureTableAsync(Guid.Empty, Math.Clamp(_ai.EmbeddingDimensions, 1, 16000), cancellationToken);

    public async Task UpsertAsync(IReadOnlyList<VectorPoint> points, CancellationToken cancellationToken = default)
    {
        if (points.Count == 0) return;
        var generationId = RequireSingleGeneration(points);
        var dimensions = points[0].Vector.Length;
        await EnsureTableAsync(generationId, dimensions, cancellationToken);
        var table = TableName(generationId);

        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var point in points)
        {
            await using var command = new NpgsqlCommand($"""
                INSERT INTO {table} (chunk_id, knowledge_base_id, document_id, embedding)
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

    public Task<IReadOnlyList<VectorCandidate>> SearchAsync(
        float[] vector, Guid knowledgeBaseId, int topK, CancellationToken cancellationToken = default)
        => SearchAsync(vector, knowledgeBaseId, Guid.Empty, topK, cancellationToken);

    public async Task<IReadOnlyList<VectorCandidate>> SearchAsync(
        float[] vector,
        Guid knowledgeBaseId,
        Guid indexGenerationId,
        int topK,
        CancellationToken cancellationToken = default)
    {
        await EnsureTableAsync(indexGenerationId, vector.Length, cancellationToken);
        var table = TableName(indexGenerationId);
        var rows = new List<VectorCandidate>();
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""
            SELECT chunk_id, 1 - (embedding <=> CAST(@embedding AS vector)) AS score
            FROM {table}
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

    public Task DeleteAsync(IReadOnlyList<Guid> pointIds, CancellationToken cancellationToken = default)
        => DeleteAsync(Guid.Empty, pointIds, cancellationToken);

    public async Task DeleteAsync(Guid indexGenerationId, IReadOnlyList<Guid> pointIds, CancellationToken cancellationToken = default)
    {
        if (pointIds.Count == 0) return;
        RequirePostgres();
        var table = TableName(indexGenerationId);
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        if (!await TableExistsAsync(connection, table, cancellationToken)) return;
        await using var command = new NpgsqlCommand($"DELETE FROM {table} WHERE chunk_id = ANY(@ids)", connection);
        command.Parameters.AddWithValue("ids", pointIds.ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteGenerationAsync(Guid knowledgeBaseId, Guid indexGenerationId, CancellationToken cancellationToken = default)
    {
        RequirePostgres();
        var table = TableName(indexGenerationId);
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        if (indexGenerationId == Guid.Empty)
        {
            if (!await TableExistsAsync(connection, table, cancellationToken)) return;
            await using var delete = new NpgsqlCommand($"DELETE FROM {table} WHERE knowledge_base_id = @kb", connection);
            delete.Parameters.AddWithValue("kb", knowledgeBaseId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        await using var drop = new NpgsqlCommand($"DROP TABLE IF EXISTS {table}", connection);
        await drop.ExecuteNonQueryAsync(cancellationToken);
        _initializedTables.TryRemove(indexGenerationId, out _);
    }

    private async Task EnsureTableAsync(Guid generationId, int dimensions, CancellationToken cancellationToken)
    {
        RequirePostgres();
        if (dimensions is < 1 or > 16000)
            throw new InvalidOperationException($"pgvector dimensions must be between 1 and 16000; received {dimensions}.");
        if (_initializedTables.TryGetValue(generationId, out var knownDimensions))
        {
            if (knownDimensions != dimensions)
                throw new InvalidOperationException($"Index generation expects {knownDimensions} embedding dimensions, received {dimensions}.");
            return;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initializedTables.TryGetValue(generationId, out knownDimensions))
            {
                if (knownDimensions != dimensions)
                    throw new InvalidOperationException($"Index generation expects {knownDimensions} embedding dimensions, received {dimensions}.");
                return;
            }

            var table = TableName(generationId);
            var (kbIndex, vectorIndex) = IndexNames(generationId);
            await using var connection = new NpgsqlConnection(_database.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using (var extension = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector", connection))
                await extension.ExecuteNonQueryAsync(cancellationToken);
            var ddl = $"""
                CREATE TABLE IF NOT EXISTS {table} (
                    chunk_id uuid PRIMARY KEY,
                    knowledge_base_id uuid NOT NULL,
                    document_id uuid NOT NULL,
                    embedding vector({dimensions}) NOT NULL
                );
                CREATE INDEX IF NOT EXISTS {kbIndex} ON {table} (knowledge_base_id);
                CREATE INDEX IF NOT EXISTS {vectorIndex}
                    ON {table} USING hnsw (embedding vector_cosine_ops);
                """;
            await using var command = new NpgsqlCommand(ddl, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
            _initializedTables[generationId] = dimensions;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT to_regclass(@tableName) IS NOT NULL", connection);
        command.Parameters.AddWithValue("tableName", table);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private void RequirePostgres()
    {
        if (!_database.Provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase) &&
            !_database.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SearchBackend:Vector=pgvector 仅能在 Database:Provider=PostgreSql 时使用。");
    }

    private static string TableName(Guid generationId)
        => generationId == Guid.Empty ? "rag_chunk_vectors" : $"rag_chunk_vectors_{generationId:N}";

    private static (string KnowledgeBaseIndex, string VectorIndex) IndexNames(Guid generationId)
        => generationId == Guid.Empty
            ? ("ix_rag_chunk_vectors_kb", "ix_rag_chunk_vectors_embedding_hnsw")
            : ($"ix_rag_{generationId:N}_kb", $"ix_rag_{generationId:N}_hnsw");

    private static Guid RequireSingleGeneration(IReadOnlyList<VectorPoint> points)
    {
        var generationId = points[0].IndexGenerationId;
        var dimensions = points[0].Vector.Length;
        if (dimensions is < 1 or > 16000) throw new InvalidOperationException("pgvector embedding dimensions are invalid.");
        if (points.Any(x => x.IndexGenerationId != generationId || x.Vector.Length != dimensions))
            throw new InvalidOperationException("A pgvector upsert batch must contain one index generation and one vector dimension.");
        return generationId;
    }

    private static string ToVectorLiteral(IEnumerable<float> values)
        => "[" + string.Join(',', values.Select(x => x.ToString("R", CultureInfo.InvariantCulture))) + "]";
}
