using DotNetCore.CAP;
using Microsoft.AspNetCore.Http;
using KnowledgeRag.Infrastructure.Persistence;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.Documents;

public sealed class KnowledgeIngestionService(
    AppDbContext db,
    IFileStorage fileStorage,
    DocumentParser parser,
    TextChunker chunker,
    OpenAiCompatibleEmbeddingService embeddings,
    IVectorStore vectorStore,
    ICapPublisher capPublisher,
    IDocumentProcessingNotifier notifier,
    IOptions<RagOptions> ragOptions,
    IOptions<DatabaseOptions> databaseOptions,
    ILogger<KnowledgeIngestionService> logger)
{
    private readonly RagOptions _rag = ragOptions.Value;
    private readonly DatabaseOptions _database = databaseOptions.Value;

    public async Task<DocumentEntity> QueueAsync(Guid knowledgeBaseId, Guid userId, IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file.Length == 0) throw new InvalidOperationException("文件为空。");
        if (file.Length > _rag.MaxUploadMegabytes * 1024L * 1024L) throw new InvalidOperationException($"文件超过 {_rag.MaxUploadMegabytes} MB 限制。");
        if (!parser.Supports(file.FileName)) throw new InvalidOperationException("暂不支持该文件格式。支持 txt/md/csv/json/xml/html/pdf/docx。");

        var documentId = Guid.NewGuid();
        var saved = await fileStorage.SaveAsync(knowledgeBaseId, documentId, file, cancellationToken);
        try
        {
            var duplicate = await db.Documents.AsNoTracking().AnyAsync(
                x => x.KnowledgeBaseId == knowledgeBaseId && x.Sha256 == saved.Sha256 &&
                     (x.Status == DocumentStatus.Queued || x.Status == DocumentStatus.Processing || x.Status == DocumentStatus.Ready),
                cancellationToken);
            if (duplicate)
            {
                await fileStorage.DeleteAsync(knowledgeBaseId, saved.StorageKey, cancellationToken);
                throw new InvalidOperationException("该知识库中已存在内容相同或正在处理的文档。");
            }

            var now = DateTimeOffset.UtcNow;
            var document = new DocumentEntity
            {
                Id = documentId,
                KnowledgeBaseId = knowledgeBaseId,
                CreatedByUserId = userId,
                FileName = Path.GetFileName(file.FileName),
                ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                StoredFileName = saved.StorageKey,
                Sha256 = saved.Sha256,
                SizeBytes = file.Length,
                Status = DocumentStatus.Queued,
                CreatedAt = now,
                UpdatedAt = now
            };

            try
            {
                await PersistAndPublishAsync(document, userId, isNew: true, cancellationToken);
            }
            catch
            {
                db.ChangeTracker.Clear();
                var persisted = await db.Documents.SingleOrDefaultAsync(x => x.Id == document.Id, CancellationToken.None);
                if (persisted is not null)
                {
                    db.Documents.Remove(persisted);
                    await db.SaveChangesAsync(CancellationToken.None);
                }
                await fileStorage.DeleteAsync(knowledgeBaseId, saved.StorageKey, CancellationToken.None);
                throw;
            }

            await NotifySafeAsync(document, userId, 0, "queued", cancellationToken);
            return document;
        }
        finally
        {
            if (saved.DeleteParseFileWhenDone && File.Exists(saved.ParseFilePath)) File.Delete(saved.ParseFilePath);
        }
    }

    public async Task<DocumentEntity> RequeueAsync(Guid knowledgeBaseId, Guid documentId, Guid userId, bool allowReady, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.SingleOrDefaultAsync(x => x.Id == documentId && x.KnowledgeBaseId == knowledgeBaseId, cancellationToken)
            ?? throw new KeyNotFoundException("文档不存在。");
        if (document.Status is DocumentStatus.Queued or DocumentStatus.Processing)
            throw new InvalidOperationException("文档已经在处理队列中。");
        if (document.Status == DocumentStatus.Ready && !allowReady)
            throw new InvalidOperationException("Ready 文档请使用重新索引操作。");
        if (document.Status is not (DocumentStatus.Failed or DocumentStatus.Cancelled or DocumentStatus.Ready))
            throw new InvalidOperationException("当前状态不能重新加入索引队列。");

        document.Status = DocumentStatus.Queued;
        document.LastError = null;
        document.UpdatedAt = DateTimeOffset.UtcNow;
        await PersistAndPublishAsync(document, userId, isNew: false, cancellationToken);
        await NotifySafeAsync(document, userId, 0, allowReady ? "reindex_queued" : "retry_queued", cancellationToken);
        return document;
    }

    public async Task<DocumentEntity> CancelAsync(Guid knowledgeBaseId, Guid documentId, Guid userId, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.SingleOrDefaultAsync(x => x.Id == documentId && x.KnowledgeBaseId == knowledgeBaseId, cancellationToken)
            ?? throw new KeyNotFoundException("文档不存在。");
        if (document.Status is not (DocumentStatus.Queued or DocumentStatus.Processing))
            throw new InvalidOperationException("只有排队中或处理中的任务可以取消。");
        document.Status = DocumentStatus.Cancelled;
        document.LastError = null;
        document.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await NotifySafeAsync(document, userId, 100, "cancelled", cancellationToken);
        return document;
    }

    private async Task PersistAndPublishAsync(DocumentEntity document, Guid userId, bool isNew, CancellationToken cancellationToken)
    {
        var message = new DocumentIndexRequested(document.Id, document.KnowledgeBaseId, userId);
        if (_database.Provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase) || _database.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        {
            using var transaction = db.Database.BeginTransaction(capPublisher, autoCommit: false);
            if (isNew) db.Documents.Add(document);
            await db.SaveChangesAsync(cancellationToken);
            await capPublisher.PublishAsync(DocumentEventTopics.IndexRequested, message);
            transaction.Commit();
        }
        else
        {
            if (isNew) db.Documents.Add(document);
            await db.SaveChangesAsync(cancellationToken);
            await capPublisher.PublishAsync(DocumentEventTopics.IndexRequested, message);
        }
    }

    public async Task ProcessAsync(DocumentIndexRequested request, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("rag.ingest.background");
        activity?.SetTag("rag.document_id", request.DocumentId.ToString());
        activity?.SetTag("rag.knowledge_base_id", request.KnowledgeBaseId.ToString());
        var document = await db.Documents.SingleOrDefaultAsync(x => x.Id == request.DocumentId && x.KnowledgeBaseId == request.KnowledgeBaseId, cancellationToken);
        if (document is null || document.Status is DocumentStatus.Ready or DocumentStatus.Cancelled) return;

        var targetVersion = DocumentIndexVersion.Next(document.IndexVersion);
        activity?.SetTag("rag.index.previous_version", document.IndexVersion);
        activity?.SetTag("rag.index.target_version", targetVersion);
        var stagedPointIds = new List<Guid>();
        string? parsePath = null;
        try
        {
            document.Status = DocumentStatus.Processing;
            document.LastError = null;
            document.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await NotifySafeAsync(document, request.UserId, 5, "processing", cancellationToken);
            await EnsureNotCancelledAsync(document, cancellationToken);

            await using var source = await fileStorage.OpenReadAsync(document.KnowledgeBaseId, document.StoredFileName, cancellationToken)
                ?? throw new FileNotFoundException("原始文档文件不存在，无法继续索引。", document.FileName);
            parsePath = Path.Combine(Path.GetTempPath(), $"knowledge-rag-{document.Id:N}{Path.GetExtension(document.FileName)}");
            await using (var target = File.Create(parsePath)) await source.CopyToAsync(target, cancellationToken);
            await EnsureNotCancelledAsync(document, cancellationToken);

            await NotifySafeAsync(document, request.UserId, 15, "parsing", cancellationToken);
            var text = await parser.ExtractTextAsync(parsePath, cancellationToken);
            await EnsureNotCancelledAsync(document, cancellationToken);
            await NotifySafeAsync(document, request.UserId, 30, "chunking", cancellationToken);
            var chunks = chunker.Split(text);
            if (chunks.Count == 0) throw new InvalidOperationException("没有从文件中提取到可索引文本。");
            activity?.SetTag("rag.chunk_count", chunks.Count);
            await EnsureNotCancelledAsync(document, cancellationToken);

            // Stable generation ids make retries idempotent and let us remove points left by a
            // crashed attempt before starting the same generation again.
            var targetPointIds = Enumerable.Range(0, chunks.Count)
                .Select(i => DocumentIndexVersion.CreatePointId(document.Id, targetVersion, i))
                .ToList();
            await CleanupVectorsAsync(targetPointIds, document.Id);

            var chunkEntities = new List<DocumentChunkEntity>(chunks.Count);
            for (var offset = 0; offset < chunks.Count; offset += _rag.EmbeddingBatchSize)
            {
                await EnsureNotCancelledAsync(document, cancellationToken);
                var batch = chunks.Skip(offset).Take(_rag.EmbeddingBatchSize).ToList();
                var vectors = await embeddings.GenerateBatchAsync(batch, cancellationToken);
                var points = new List<VectorPoint>(batch.Count);
                for (var i = 0; i < batch.Count; i++)
                {
                    var chunkIndex = offset + i;
                    var chunkId = targetPointIds[chunkIndex];
                    stagedPointIds.Add(chunkId);
                    chunkEntities.Add(new DocumentChunkEntity
                    {
                        Id = chunkId,
                        KnowledgeBaseId = document.KnowledgeBaseId,
                        DocumentId = document.Id,
                        ChunkIndex = chunkIndex,
                        Text = batch[i],
                        CharacterCount = batch[i].Length,
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                    points.Add(new VectorPoint(chunkId, document.KnowledgeBaseId, document.Id, document.FileName, chunkIndex, vectors[i]));
                }
                foreach (var upsertBatch in points.Chunk(_rag.UpsertBatchSize)) await vectorStore.UpsertAsync(upsertBatch, cancellationToken);
                var completed = Math.Min(offset + batch.Count, chunks.Count);
                var progress = 40 + (int)Math.Round(45d * completed / chunks.Count);
                await NotifySafeAsync(document, request.UserId, progress, "embedding", cancellationToken, chunkEntities.Count);
            }

            await EnsureNotCancelledAsync(document, cancellationToken);
            await NotifySafeAsync(document, request.UserId, 90, "storing", cancellationToken, chunkEntities.Count);

            // The old chunk set stays queryable for the whole build. The database transaction is
            // the activation boundary: readers observe either the complete old set or complete new set.
            var oldChunks = await db.DocumentChunks.Where(x => x.DocumentId == document.Id).ToListAsync(cancellationToken);
            var oldPointIds = oldChunks.Select(x => x.Id).ToList();
            await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
            {
                if (oldChunks.Count > 0) db.DocumentChunks.RemoveRange(oldChunks);
                db.DocumentChunks.AddRange(chunkEntities);
                document.ChunkCount = chunkEntities.Count;
                document.IndexVersion = targetVersion;
                document.Status = DocumentStatus.Ready;
                document.LastError = null;
                document.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            activity?.SetTag("rag.index.activated_version", targetVersion);
            await NotifySafeAsync(document, request.UserId, 100, "completed", cancellationToken, document.ChunkCount);

            // Old vectors are no longer reachable through the active DB chunk set. Cleanup is
            // deliberately best-effort so an external vector-store outage cannot roll back activation.
            await CleanupVectorsAsync(oldPointIds, document.Id);
        }
        catch (DocumentProcessingCancelledException)
        {
            await CleanupVectorsAsync(stagedPointIds, document.Id);
            DetachDocumentChunks(document.Id);
            await db.Entry(document).ReloadAsync(CancellationToken.None);
            document.Status = DocumentStatus.Cancelled;
            document.LastError = null;
            document.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            await NotifySafeAsync(document, request.UserId, 100, "cancelled", CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            await CleanupVectorsAsync(stagedPointIds, document.Id);
            DetachDocumentChunks(document.Id);
            try { await db.Entry(document).ReloadAsync(CancellationToken.None); } catch { }
            document.Status = DocumentStatus.Failed;
            document.LastError = ex.Message.Length <= 4000 ? ex.Message : ex.Message[..4000];
            document.UpdatedAt = DateTimeOffset.UtcNow;
            try { await db.SaveChangesAsync(CancellationToken.None); }
            catch (Exception saveEx) { logger.LogError(saveEx, "Failed to persist indexing failure for document {DocumentId}", document.Id); }
            await NotifySafeAsync(document, request.UserId, 100, "failed", CancellationToken.None, error: document.LastError);
            throw;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(parsePath) && File.Exists(parsePath)) { try { File.Delete(parsePath); } catch { } }
        }
    }

    private async Task EnsureNotCancelledAsync(DocumentEntity document, CancellationToken cancellationToken)
    {
        await db.Entry(document).ReloadAsync(cancellationToken);
        if (document.Status == DocumentStatus.Cancelled) throw new DocumentProcessingCancelledException();
    }

    private void DetachDocumentChunks(Guid documentId)
    {
        foreach (var entry in db.ChangeTracker.Entries<DocumentChunkEntity>().Where(x => x.Entity.DocumentId == documentId).ToList())
            entry.State = EntityState.Detached;
    }

    private async Task CleanupVectorsAsync(IReadOnlyCollection<Guid> pointIds, Guid documentId)
    {
        if (pointIds.Count == 0) return;
        try { await vectorStore.DeleteAsync(pointIds.ToList(), CancellationToken.None); }
        catch (Exception cleanupEx) { logger.LogWarning(cleanupEx, "Failed to clean vectors for document {DocumentId}", documentId); }
    }

    public async Task<IReadOnlyList<DocumentEntity>> ListAsync(Guid knowledgeBaseId, CancellationToken cancellationToken = default)
    {
        var rows = await db.Documents.AsNoTracking().Where(x => x.KnowledgeBaseId == knowledgeBaseId).ToListAsync(cancellationToken);
        return rows.OrderByDescending(x => x.CreatedAt).ToList();
    }

    public async Task<DocumentDownload?> OpenReadAsync(Guid knowledgeBaseId, Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == documentId && x.KnowledgeBaseId == knowledgeBaseId, cancellationToken);
        if (document is null) return null;
        var stream = await fileStorage.OpenReadAsync(knowledgeBaseId, document.StoredFileName, cancellationToken);
        return stream is null ? null : new DocumentDownload(stream, document.ContentType, document.FileName);
    }

    public async Task<bool> DeleteAsync(Guid knowledgeBaseId, Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.SingleOrDefaultAsync(x => x.Id == documentId && x.KnowledgeBaseId == knowledgeBaseId, cancellationToken);
        if (document is null) return false;
        if (document.Status is DocumentStatus.Queued or DocumentStatus.Processing)
            throw new InvalidOperationException("文档正在后台索引，请先取消任务或等待处理完成。");
        var pointIds = await db.DocumentChunks.AsNoTracking().Where(x => x.DocumentId == documentId).Select(x => x.Id).ToListAsync(cancellationToken);
        await vectorStore.DeleteAsync(pointIds, cancellationToken);
        await fileStorage.DeleteAsync(knowledgeBaseId, document.StoredFileName, cancellationToken);
        db.Documents.Remove(document);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task NotifySafeAsync(DocumentEntity document, Guid userId, int progress, string stage, CancellationToken cancellationToken, int? chunkCount = null, string? error = null)
    {
        try
        {
            await notifier.NotifyAsync(new DocumentProcessingNotification(document.Id, document.KnowledgeBaseId, userId, document.FileName,
                document.Status, Math.Clamp(progress, 0, 100), stage, chunkCount ?? document.ChunkCount, error, DateTimeOffset.UtcNow), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "SignalR document notification failed for {DocumentId}", document.Id);
        }
    }

    private sealed class DocumentProcessingCancelledException : Exception;
}

public sealed record DocumentDownload(Stream Stream, string ContentType, string FileName);
