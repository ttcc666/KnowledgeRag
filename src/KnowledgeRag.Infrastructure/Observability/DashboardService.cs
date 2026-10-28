using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Application.Security;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Configuration;
using KnowledgeRag.Infrastructure.KnowledgeBases;
using KnowledgeRag.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.Observability;

public sealed class DashboardService(AppDbContext db, KnowledgeBaseAccessService access, ProductMetricsCollector metrics)
{
    public async Task<DashboardSummary> GetSummaryAsync(ActorContext actor, CancellationToken cancellationToken = default)
    {
        var ids = await access.GetAccessibleKnowledgeBaseIdsAsync(actor, cancellationToken);
        if (ids.Count == 0)
            return new DashboardSummary(0, 0, 0, 0, 0, 0, 0, BuildEmptyActivity(), [], metrics.Snapshot());

        var knowledgeBases = await db.KnowledgeBases.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToListAsync(cancellationToken);
        var docs = await db.Documents.AsNoTracking().Where(x => ids.Contains(x.KnowledgeBaseId)).ToListAsync(cancellationToken);
        var conversationsQuery = db.Conversations.AsNoTracking().Where(x => ids.Contains(x.KnowledgeBaseId) && x.UserId == actor.UserId);
        conversationsQuery = actor.IsApiKey ? conversationsQuery.Where(x => x.ApiKeyId == actor.ApiKeyId) : conversationsQuery.Where(x => x.ApiKeyId == null);
        var conversations = await conversationsQuery.ToListAsync(cancellationToken);
        var names = knowledgeBases.ToDictionary(x => x.Id, x => x.Name);
        var now = DateTimeOffset.UtcNow;
        var activity = Enumerable.Range(0, 7).Select(offset =>
        {
            var date = DateOnly.FromDateTime(now.UtcDateTime.Date.AddDays(offset - 6));
            return new DailyActivityPoint(date.ToString("yyyy-MM-dd"),
                docs.Count(x => DateOnly.FromDateTime(x.UpdatedAt.UtcDateTime) == date),
                conversations.Count(x => DateOnly.FromDateTime(x.UpdatedAt.UtcDateTime) == date));
        }).ToList();
        var jobs = BuildJobs(docs, names, now);
        return new DashboardSummary(
            knowledgeBases.Count,
            docs.Count(x => x.Status is DocumentStatus.Ready or DocumentStatus.Reindexing),
            docs.Count(x => x.Status is DocumentStatus.Queued or DocumentStatus.Processing or DocumentStatus.Reindexing),
            docs.Count(x => x.Status == DocumentStatus.Failed),
            docs.Count(x => x.Status == DocumentStatus.Cancelled),
            docs.Sum(x => x.ChunkCount),
            conversations.Count,
            activity,
            jobs.OrderByDescending(x => x.UpdatedAt).Take(12).ToList(),
            metrics.Snapshot());
    }

    public async Task<IReadOnlyList<IngestionJobSummary>> ListJobsAsync(ActorContext actor, CancellationToken cancellationToken = default)
    {
        var ids = await access.GetAccessibleKnowledgeBaseIdsAsync(actor, cancellationToken);
        if (ids.Count == 0) return [];
        var names = await db.KnowledgeBases.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var docs = await db.Documents.AsNoTracking().Where(x => ids.Contains(x.KnowledgeBaseId)).ToListAsync(cancellationToken);
        return BuildJobs(docs, names, DateTimeOffset.UtcNow).OrderByDescending(x => x.UpdatedAt).Take(500).ToList();
    }

    private static IReadOnlyList<IngestionJobSummary> BuildJobs(IReadOnlyList<DocumentEntity> docs, IReadOnlyDictionary<Guid, string> names, DateTimeOffset now)
    {
        var queued = docs.Where(x => x.Status == DocumentStatus.Queued).OrderBy(x => x.CreatedAt).Select((doc, index) => new { doc.Id, Position = index + 1 }).ToDictionary(x => x.Id, x => x.Position);
        return docs.Select(doc => new IngestionJobSummary(
            doc.Id,
            doc.KnowledgeBaseId,
            names.GetValueOrDefault(doc.KnowledgeBaseId, "Unknown"),
            doc.FileName,
            doc.Status,
            doc.ChunkCount,
            doc.LastError,
            doc.CreatedAt,
            doc.UpdatedAt,
            Math.Max(0, ((doc.Status is DocumentStatus.Queued or DocumentStatus.Processing or DocumentStatus.Reindexing ? now : doc.UpdatedAt) - doc.CreatedAt).TotalSeconds),
            queued.TryGetValue(doc.Id, out var position) ? position : null)).ToList();
    }

    private static IReadOnlyList<DailyActivityPoint> BuildEmptyActivity()
    {
        var today = DateTime.UtcNow.Date;
        return Enumerable.Range(0, 7).Select(i => new DailyActivityPoint(DateOnly.FromDateTime(today.AddDays(i - 6)).ToString("yyyy-MM-dd"), 0, 0)).ToList();
    }
}

public sealed class ProductSettingsService(
    IOptions<DatabaseOptions> database,
    IOptions<AiOptions> ai,
    IOptions<RagOptions> rag,
    IOptions<SearchBackendOptions> search,
    IOptions<RerankerOptions> reranker,
    IOptions<StorageOptions> storage,
    IOptions<CapIntegrationOptions> cap,
    IOptions<OpenTelemetryOptions> otel)
{
    public ProductSettingsSummary Get() => new(
        database.Value.Provider,
        ai.Value.ChatModel,
        ai.Value.EmbeddingModel,
        search.Value.Vector,
        search.Value.Lexical,
        reranker.Value.Mode,
        storage.Value.Provider,
        cap.Value.Transport,
        cap.Value.FailedRetryCount,
        rag.Value.ChunkSize,
        rag.Value.ChunkOverlap,
        rag.Value.TopK,
        rag.Value.MaxUploadMegabytes,
        otel.Value.Enabled);
}
