using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Configuration;
using KnowledgeRag.Infrastructure.Persistence;
using KnowledgeRag.Infrastructure.Search;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class Bm25SearchServiceSqliteTests
{
    [Fact]
    public async Task SearchAsync_WithSqlite_DoesNotOrderDateTimeOffsetThroughEf()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AppDbContext(dbOptions);
        await db.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        var creatorId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var knowledgeBaseId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();
        var stagedChunkId = Guid.NewGuid();
        var stagedGenerationId = Guid.NewGuid();

        db.Tenants.Add(new TenantEntity
        {
            Id = tenantId,
            Name = "SQLite BM25 Tenant",
            Slug = $"sqlite-bm25-{Guid.NewGuid():N}",
            CreatedByUserId = creatorId,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.KnowledgeBases.Add(new KnowledgeBaseEntity
        {
            Id = knowledgeBaseId,
            TenantId = tenantId,
            Name = "SQLite BM25 KB",
            CreatedByUserId = creatorId,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Documents.Add(new DocumentEntity
        {
            Id = documentId,
            KnowledgeBaseId = knowledgeBaseId,
            CreatedByUserId = creatorId,
            FileName = "sqlite-bm25.txt",
            ContentType = "text/plain",
            StoredFileName = "sqlite-bm25.txt",
            Sha256 = new string('a', 64),
            SizeBytes = 32,
            ChunkCount = 1,
            Status = DocumentStatus.Ready,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.DocumentChunks.AddRange(
            new DocumentChunkEntity
            {
                Id = chunkId,
                KnowledgeBaseId = knowledgeBaseId,
                DocumentId = documentId,
                ChunkIndex = 0,
                Text = "sqlite bm25 ordering regression token",
                CharacterCount = 37,
                CreatedAt = now
            },
            new DocumentChunkEntity
            {
                Id = stagedChunkId,
                KnowledgeBaseId = knowledgeBaseId,
                DocumentId = documentId,
                IndexGenerationId = stagedGenerationId,
                ChunkIndex = 0,
                Text = "sqlite bm25 staging regression token",
                CharacterCount = 36,
                CreatedAt = now.AddSeconds(1)
            });
        await db.SaveChangesAsync();

        var service = new Bm25SearchService(
            db,
            Options.Create(new RagOptions { Bm25MaxCorpusChunks = 1_000 }));

        var results = await service.SearchAsync(knowledgeBaseId, "regression", 5);

        var hit = Assert.Single(results);
        Assert.Equal(chunkId, hit.PointId);
        Assert.Equal(documentId, hit.DocumentId);
        Assert.Equal("sqlite-bm25.txt", hit.FileName);
        Assert.True(hit.Bm25Score > 0);

        var stagedResults = await service.SearchAsync(knowledgeBaseId, stagedGenerationId, "regression", 5);
        var stagedHit = Assert.Single(stagedResults);
        Assert.Equal(stagedChunkId, stagedHit.PointId);
        Assert.NotEqual(hit.PointId, stagedHit.PointId);
    }
}
