using System.Text;
using KnowledgeRag.Infrastructure.Persistence;
using KnowledgeRag.Application.Abstractions;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Configuration;
using KnowledgeRag.Infrastructure.Search;
using KnowledgeRag.Infrastructure.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class ExternalBackendTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("RUN_EXTERNAL_BACKEND_TESTS") == "1";

    [Fact, Trait("Category", "ExternalBackend")]
    public async Task Minio_S3FileStorage_RoundTripsObject()
    {
        if (!Enabled) return;
        var root = Path.Combine(Path.GetTempPath(), $"knowledge-rag-s3-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var http = new HttpClient();
            var storage = new S3FileStorage(
                http,
                Options.Create(new S3Options
                {
                    Endpoint = Environment.GetEnvironmentVariable("EXTERNAL_S3_ENDPOINT") ?? "http://localhost:9000",
                    Region = Environment.GetEnvironmentVariable("S3_REGION") ?? "us-east-1",
                    Bucket = Environment.GetEnvironmentVariable("S3_BUCKET") ?? "knowledge-rag",
                    AccessKey = Environment.GetEnvironmentVariable("S3_ACCESS_KEY") ?? "minioadmin",
                    SecretKey = Environment.GetEnvironmentVariable("S3_SECRET_KEY") ?? "minioadmin",
                    ForcePathStyle = true
                }),
                Options.Create(new StorageOptions { Provider = "s3", RootPath = root }),
                new TestWebHostEnvironment(root));

            var kbId = Guid.NewGuid();
            var documentId = Guid.NewGuid();
            var content = Encoding.UTF8.GetBytes("minio integration round trip");
            await using var uploadStream = new MemoryStream(content);
            IFormFile form = new FormFile(uploadStream, 0, content.Length, "file", "probe.txt")
            {
                Headers = new HeaderDictionary(),
                ContentType = "text/plain"
            };

            var stored = await storage.SaveAsync(kbId, documentId, form);
            Assert.False(string.IsNullOrWhiteSpace(stored.Sha256));
            Assert.True(File.Exists(stored.ParseFilePath));

            await using (var download = await storage.OpenReadAsync(kbId, stored.StorageKey))
            {
                Assert.NotNull(download);
                using var reader = new StreamReader(download!, Encoding.UTF8);
                Assert.Equal("minio integration round trip", await reader.ReadToEndAsync());
            }

            await storage.DeleteAsync(kbId, stored.StorageKey);
            Assert.Null(await storage.OpenReadAsync(kbId, stored.StorageKey));
            if (stored.DeleteParseFileWhenDone && File.Exists(stored.ParseFilePath)) File.Delete(stored.ParseFilePath);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact, Trait("Category", "ExternalBackend")]
    public async Task PgVectorStore_UpsertSearchDelete_Works()
    {
        if (!Enabled) return;
        var connection = ExternalConnectionString();
        var vectorStore = new PgVectorStore(
            Options.Create(new DatabaseOptions { Provider = "PostgreSql", ConnectionString = connection }),
            Options.Create(new AiOptions { EmbeddingDimensions = 1536 }));

        var kbId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = new float[1536]; first[0] = 1;
        var second = new float[1536]; second[1] = 1;
        try
        {
            await vectorStore.UpsertAsync([
                new VectorPoint(firstId, kbId, documentId, "a.txt", 0, first),
                new VectorPoint(secondId, kbId, documentId, "a.txt", 1, second)
            ]);
            var hits = await vectorStore.SearchAsync(first, kbId, 2);
            Assert.NotEmpty(hits);
            Assert.Equal(firstId, hits[0].PointId);
            Assert.True(hits[0].Score > 0.99);
        }
        finally
        {
            await vectorStore.DeleteAsync([firstId, secondId]);
        }
    }

    [Fact, Trait("Category", "ExternalBackend")]
    public async Task PostgresFts_FindsPersistedReadyChunk()
    {
        if (!Enabled) return;
        var connection = ExternalConnectionString();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options;
        await using var db = new AppDbContext(dbOptions);
        var now = DateTimeOffset.UtcNow;
        var email = $"fts-{Guid.NewGuid():N}@example.com";
        var user = new UserEntity
        {
            Id = Guid.NewGuid(), Email = email, NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = "FTS Test", PasswordHash = "not-used", PasswordSalt = "not-used", IsActive = true, CreatedAt = now
        };
        var tenant = new TenantEntity
        {
            Id = Guid.NewGuid(), Name = "FTS Test Tenant", Slug = $"fts-{Guid.NewGuid():N}", CreatedByUserId = user.Id, CreatedAt = now, UpdatedAt = now
        };
        var kb = new KnowledgeBaseEntity
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "FTS KB", CreatedByUserId = user.Id, CreatedAt = now, UpdatedAt = now
        };
        var document = new DocumentEntity
        {
            Id = Guid.NewGuid(), KnowledgeBaseId = kb.Id, CreatedByUserId = user.Id, FileName = "policy.txt", ContentType = "text/plain",
            StoredFileName = "probe", Sha256 = new string('a', 64), SizeBytes = 32, ChunkCount = 1, Status = DocumentStatus.Ready, CreatedAt = now, UpdatedAt = now
        };
        var chunk = new DocumentChunkEntity
        {
            Id = Guid.NewGuid(), KnowledgeBaseId = kb.Id, DocumentId = document.Id, ChunkIndex = 0,
            Text = "invoice approval policy requires manager review", CharacterCount = 47, CreatedAt = now
        };
        db.AddRange(user, tenant,
            new TenantMemberEntity { TenantId = tenant.Id, UserId = user.Id, Role = TenantRole.Owner, AddedAt = now }, kb,
            new KnowledgeBaseMemberEntity { KnowledgeBaseId = kb.Id, UserId = user.Id, Role = KnowledgeBaseRole.Owner, AddedAt = now },
            document, chunk);
        await db.SaveChangesAsync();

        try
        {
            var service = new PostgresFtsSearchService(
                Options.Create(new DatabaseOptions { Provider = "PostgreSql", ConnectionString = connection }),
                Options.Create(new SearchBackendOptions { Lexical = "postgres_fts", PostgresTextSearchConfig = "simple" }));
            var results = await service.SearchAsync(kb.Id, "invoice", 10);
            var hit = Assert.Single(results);
            Assert.Equal(chunk.Id, hit.PointId);
            Assert.True(hit.Bm25Score is > 0);
        }
        finally
        {
            db.Tenants.Remove(tenant);
            await db.SaveChangesAsync();
            db.Users.Remove(user);
            await db.SaveChangesAsync();
        }
    }

    private static string ExternalConnectionString()
        => Environment.GetEnvironmentVariable("EXTERNAL_DATABASE_CONNECTION_STRING")
           ?? "Host=localhost;Port=5432;Database=knowledge_rag;Username=knowledge_rag;Password=knowledge_rag_dev_password";

    private sealed class TestWebHostEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "KnowledgeRag.Api.IntegrationTests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(root);
    }
}
