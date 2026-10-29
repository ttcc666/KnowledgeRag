using System.Text.RegularExpressions;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Application.Security;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KnowledgeRag.Infrastructure.Search;

public sealed class PostgresFtsSearchService : ILexicalSearchService
{
    private readonly DatabaseOptions _database;
    private readonly SearchBackendOptions _options;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public PostgresFtsSearchService(IOptions<DatabaseOptions> database, IOptions<SearchBackendOptions> options)
    {
        _database = database.Value;
        _options = options.Value;
    }

    public async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        RequirePostgres();
        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            var config = ValidateConfig(_options.PostgresTextSearchConfig);
            await using var connection = new NpgsqlConnection(_database.ConnectionString);
            await connection.OpenAsync(ct);
            var sql = $"CREATE INDEX IF NOT EXISTS ix_document_chunks_fts ON \"DocumentChunks\" USING GIN (to_tsvector('{config}'::regconfig, \"Text\"));";
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(ct);
            _initialized = true;
        }
        finally { _initLock.Release(); }
    }

    public Task<IReadOnlyList<HybridCandidate>> SearchAsync(Guid knowledgeBaseId, string query, int limit, CancellationToken cancellationToken = default)
        => SearchAsync(knowledgeBaseId, Guid.Empty, query, limit, cancellationToken);

    public async Task<IReadOnlyList<HybridCandidate>> SearchAsync(Guid knowledgeBaseId, Guid indexGenerationId, string query, int limit, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        await EnsureReadyAsync(cancellationToken);
        var config = ValidateConfig(_options.PostgresTextSearchConfig);
        var rows = new List<HybridCandidate>();
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var sql = $"""
            SELECT c."Id", c."DocumentId", d."FileName", c."ChunkIndex", c."Text",
                   ts_rank_cd(to_tsvector('{config}'::regconfig, c."Text"), plainto_tsquery('{config}'::regconfig, @query)) AS score
            FROM "DocumentChunks" c
            JOIN "Documents" d ON d."Id" = c."DocumentId"
            WHERE c."KnowledgeBaseId" = @kb
              AND c."IndexGenerationId" = @generation
              AND d."ChunkCount" > 0
              AND to_tsvector('{config}'::regconfig, c."Text") @@ plainto_tsquery('{config}'::regconfig, @query)
            ORDER BY score DESC
            LIMIT @limit
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("query", query);
        command.Parameters.AddWithValue("kb", knowledgeBaseId);
        command.Parameters.AddWithValue("generation", indexGenerationId);
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 1000));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var chunkId = reader.GetGuid(0);
            var documentId = reader.GetGuid(1);
            var score = Convert.ToDouble(reader.GetValue(5), System.Globalization.CultureInfo.InvariantCulture);
            rows.Add(new HybridCandidate(chunkId, documentId, reader.GetString(2), reader.GetInt32(3), reader.GetString(4),
                $"/api/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/file", null, score, 0));
        }
        return rows;
    }

    private void RequirePostgres()
    {
        if (!_database.Provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase) && !_database.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SearchBackend:Lexical=postgres_fts 仅能在 Database:Provider=PostgreSql 时使用。");
    }

    private static string ValidateConfig(string value)
    {
        var config = string.IsNullOrWhiteSpace(value) ? "simple" : value.Trim().ToLowerInvariant();
        if (!Regex.IsMatch(config, "^[a-z_]+$")) throw new InvalidOperationException("PostgreSQL FTS config 包含非法字符。");
        return config;
    }
}
