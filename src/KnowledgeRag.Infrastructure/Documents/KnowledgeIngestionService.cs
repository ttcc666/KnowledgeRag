using DotNetCore.CAP;
using Microsoft.AspNetCore.Http;
using KnowledgeRag.Infrastructure.Persistence;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KnowledgeRag.Infrastructure.Tenancy;

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
    TenantAiProviderService tenantAiProviderService,
    KnowledgeBaseIndexGenerationService indexGenerationService,
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
        // UpdatedAt is also the persisted generation seed. CAP retries of this queue request use
        // the same stable vector point ids; an explicit later requeue receives a fresh generation.
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
        var knowledgeBase = await db.KnowledgeBases.SingleAsync(x => x.Id == knowledgeBaseId, cancellationToken);
        knowledgeBase.UpdatedAt = document.UpdatedAt;
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
            var knowledgeBase = await db.KnowledgeBases.SingleAsync(x => x.Id == document.KnowledgeBaseId, cancellationToken);
            knowledgeBase.UpdatedAt = DateTimeOffset.UtcNow;
            if (isNew) db.Documents.Add(document);
            await db.SaveChangesAsync(cancellationToken);
            await capPublisher.PublishAsync(DocumentEventTopics.IndexRequested, message);
            transaction.Commit();
        }
        else
        {
            var knowledgeBase = await db.KnowledgeBases.SingleAsync(x => x.Id == document.KnowledgeBaseId, cancellationToken);
            knowledgeBase.UpdatedAt = DateTimeOffset.UtcNow;
            if (isNew) db.Documents.Add(document);
            await db.SaveChangesAsync(cancellationToken);
            await capPublisher.PublishAsync(DocumentEventTopics.IndexRequested, message);
        }
    }

    public async Task ProcessAsync(DocumentIndexRequested request, CancellationToken cancellationToken = default)
    {
        if (request.IndexGenerationId is Guid requestedGenerationId)
        {
            await ProcessGenerationDocumentAsync(request, requestedGenerationId, cancellationToken);
            return;
        }

        using var activity = Telemetry.ActivitySource.StartActivity("rag.ingest.background");
        activity?.SetTag("rag.document_id", request.DocumentId.ToString());
        activity?.SetTag("rag.knowledge_base_id", request.KnowledgeBaseId.ToString());
        var document = await db.Documents.SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.KnowledgeBaseId == request.KnowledgeBaseId, cancellationToken);
        if (document is null || document.Status is DocumentStatus.Ready or DocumentStatus.Cancelled) return;

        var activeGenerationId = await db.KnowledgeBases.AsNoTracking()
            .Where(x => x.Id == request.KnowledgeBaseId)
            .Select(x => (Guid?)x.ActiveIndexGenerationId)
            .SingleOrDefaultAsync(cancellationToken);
        if (!activeGenerationId.HasValue) return;
        var indexGenerationId = activeGenerationId.Value;
        var generationTicks = document.UpdatedAt.UtcDateTime.Ticks;
        activity?.SetTag("rag.index.generation", generationTicks);
        activity?.SetTag("rag.index.generation_id", indexGenerationId.ToString());
        activity?.SetTag("rag.index.has_active_generation", document.ChunkCount > 0);

        var stagedPointIds = new List<Guid>();
        string? parsePath = null;
        try
        {
            document.Status = DocumentStatus.Processing;
            document.LastError = null;
            await db.SaveChangesAsync(cancellationToken);
            await NotifySafeAsync(document, request.UserId, 5, "processing", cancellationToken);
            await EnsureNotCancelledAsync(document, cancellationToken);

            var embeddingEndpoint = await tenantAiProviderService.ResolveEmbeddingEndpointForGenerationAsync(
                request.KnowledgeBaseId, indexGenerationId, cancellationToken);
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

            var targetPointIds = Enumerable.Range(0, chunks.Count)
                .Select(i => DocumentIndexVersion.CreatePointId(document.Id, generationTicks, i))
                .ToList();
            await CleanupVectorsAsync(targetPointIds, document.Id, indexGenerationId);

            var chunkEntities = new List<DocumentChunkEntity>(chunks.Count);
            for (var offset = 0; offset < chunks.Count; offset += _rag.EmbeddingBatchSize)
            {
                await EnsureNotCancelledAsync(document, cancellationToken);
                var batch = chunks.Skip(offset).Take(_rag.EmbeddingBatchSize).ToList();
                var vectors = await embeddings.GenerateBatchAsync(batch, embeddingEndpoint, cancellationToken);
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
                        IndexGenerationId = indexGenerationId,
                        ChunkIndex = chunkIndex,
                        Text = batch[i],
                        CharacterCount = batch[i].Length,
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                    points.Add(new VectorPoint(chunkId, document.KnowledgeBaseId, document.Id, document.FileName, chunkIndex, vectors[i], indexGenerationId));
                }
                foreach (var upsertBatch in points.Chunk(_rag.UpsertBatchSize))
                    await vectorStore.UpsertAsync(upsertBatch, cancellationToken);
                var completed = Math.Min(offset + batch.Count, chunks.Count);
                var progress = 40 + (int)Math.Round(45d * completed / chunks.Count);
                await NotifySafeAsync(document, request.UserId, progress, "embedding", cancellationToken, chunkEntities.Count);
            }

            await EnsureNotCancelledAsync(document, cancellationToken);
            await NotifySafeAsync(document, request.UserId, 90, "storing", cancellationToken, chunkEntities.Count);

            var oldChunks = await db.DocumentChunks
                .Where(x => x.DocumentId == document.Id && x.IndexGenerationId == indexGenerationId)
                .ToListAsync(cancellationToken);
            var oldPointIds = oldChunks.Select(x => x.Id).ToList();
            await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
            {
                var knowledgeBase = await db.KnowledgeBases.SingleAsync(x => x.Id == request.KnowledgeBaseId, cancellationToken);
                if (knowledgeBase.ActiveIndexGenerationId != indexGenerationId)
                    throw new ActiveIndexGenerationChangedException();

                if (oldChunks.Count > 0) db.DocumentChunks.RemoveRange(oldChunks);
                db.DocumentChunks.AddRange(chunkEntities);
                document.ChunkCount = chunkEntities.Count;
                document.Status = DocumentStatus.Ready;
                document.LastError = null;
                document.UpdatedAt = DateTimeOffset.UtcNow;
                knowledgeBase.UpdatedAt = document.UpdatedAt;
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            activity?.SetTag("rag.index.activated", true);
            await NotifySafeAsync(document, request.UserId, 100, "completed", cancellationToken, document.ChunkCount);
            await CleanupVectorsAsync(oldPointIds, document.Id, indexGenerationId);
        }
        catch (ActiveIndexGenerationChangedException)
        {
            await CleanupVectorsAsync(stagedPointIds, request.DocumentId, indexGenerationId);
            db.ChangeTracker.Clear();
            var persisted = await db.Documents.SingleOrDefaultAsync(x => x.Id == request.DocumentId, CancellationToken.None);
            if (persisted is not null && persisted.Status != DocumentStatus.Cancelled)
            {
                persisted.Status = DocumentStatus.Queued;
                persisted.LastError = null;
                persisted.UpdatedAt = DateTimeOffset.UtcNow;
                await PersistAndPublishAsync(persisted, request.UserId, isNew: false, CancellationToken.None);
                await NotifySafeAsync(persisted, request.UserId, 0, "requeued_for_active_generation", CancellationToken.None);
            }
        }
        catch (DocumentProcessingCancelledException)
        {
            await CleanupVectorsAsync(stagedPointIds, document.Id, indexGenerationId);
            db.ChangeTracker.Clear();
            var persisted = await db.Documents.SingleAsync(x => x.Id == request.DocumentId, CancellationToken.None);
            persisted.Status = DocumentStatus.Cancelled;
            persisted.LastError = null;
            await db.SaveChangesAsync(CancellationToken.None);
            await NotifySafeAsync(persisted, request.UserId, 100, "cancelled", CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            db.ChangeTracker.Clear();
            if (await IsGenerationActiveAsync(request.DocumentId, indexGenerationId, stagedPointIds, CancellationToken.None))
            {
                var activated = await db.Documents.SingleAsync(x => x.Id == request.DocumentId, CancellationToken.None);
                activity?.SetTag("rag.index.commit_recovered", true);
                await NotifySafeAsync(activated, request.UserId, 100, "completed", CancellationToken.None, activated.ChunkCount);
                return;
            }

            await CleanupVectorsAsync(stagedPointIds, request.DocumentId, indexGenerationId);
            var persisted = await db.Documents.SingleAsync(x => x.Id == request.DocumentId, CancellationToken.None);
            persisted.Status = DocumentStatus.Failed;
            persisted.LastError = ex.Message.Length <= 4000 ? ex.Message : ex.Message[..4000];
            try { await db.SaveChangesAsync(CancellationToken.None); }
            catch (Exception saveEx) { logger.LogError(saveEx, "Failed to persist indexing failure for document {DocumentId}", request.DocumentId); }
            await NotifySafeAsync(persisted, request.UserId, 100, "failed", CancellationToken.None, error: persisted.LastError);
            throw;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(parsePath) && File.Exists(parsePath)) { try { File.Delete(parsePath); } catch { } }
        }
    }

    private async Task ProcessGenerationDocumentAsync(
        DocumentIndexRequested request,
        Guid indexGenerationId,
        CancellationToken cancellationToken)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("rag.ingest.index-generation");
        activity?.SetTag("rag.document_id", request.DocumentId.ToString());
        activity?.SetTag("rag.knowledge_base_id", request.KnowledgeBaseId.ToString());
        activity?.SetTag("rag.index.generation_id", indexGenerationId.ToString());

        var generation = await db.KnowledgeBaseIndexGenerations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == indexGenerationId && x.KnowledgeBaseId == request.KnowledgeBaseId, cancellationToken);
        if (generation is null || generation.Status != KnowledgeBaseIndexGenerationStatuses.Building) return;
        var generationDocument = await db.KnowledgeBaseIndexGenerationDocuments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IndexGenerationId == indexGenerationId && x.DocumentId == request.DocumentId, cancellationToken);
        if (generationDocument is null || generationDocument.Status == KnowledgeBaseIndexGenerationDocumentStatuses.Ready) return;

        var documentSnapshot = await db.Documents.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.DocumentId && x.KnowledgeBaseId == request.KnowledgeBaseId, cancellationToken);
        if (documentSnapshot is null)
        {
            await indexGenerationService.FailAsync(indexGenerationId, request.DocumentId, "重建期间源文档已被删除。", cancellationToken);
            return;
        }
        if (!documentSnapshot.Sha256.Equals(generationDocument.SourceSha256, StringComparison.OrdinalIgnoreCase))
        {
            await indexGenerationService.FailAsync(indexGenerationId, request.DocumentId, "源文档内容在重建期间发生变化。", cancellationToken);
            return;
        }

        var claimedAt = DateTimeOffset.UtcNow;
        var staleBefore = claimedAt.AddMinutes(-15);
        var claimed = await db.KnowledgeBaseIndexGenerationDocuments
            .Where(x => x.IndexGenerationId == indexGenerationId && x.DocumentId == request.DocumentId &&
                        (x.Status == KnowledgeBaseIndexGenerationDocumentStatuses.Pending ||
                         (x.Status == KnowledgeBaseIndexGenerationDocumentStatuses.Processing && x.UpdatedAt < staleBefore)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, KnowledgeBaseIndexGenerationDocumentStatuses.Processing)
                .SetProperty(x => x.UpdatedAt, claimedAt)
                .SetProperty(x => x.Error, (string?)null), cancellationToken);
        if (claimed == 0) return;

        db.ChangeTracker.Clear();
        var document = await db.Documents.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.DocumentId && x.KnowledgeBaseId == request.KnowledgeBaseId, cancellationToken);
        if (document is null)
        {
            await indexGenerationService.FailAsync(indexGenerationId, request.DocumentId, "重建期间源文档已被删除。", cancellationToken);
            return;
        }

        var stagedPointIds = new List<Guid>();
        string? parsePath = null;
        try
        {
            var embeddingEndpoint = await tenantAiProviderService.ResolveEmbeddingEndpointForGenerationAsync(
                request.KnowledgeBaseId, indexGenerationId, cancellationToken);
            await using var source = await fileStorage.OpenReadAsync(document.KnowledgeBaseId, document.StoredFileName, cancellationToken)
                ?? throw new FileNotFoundException("原始文档文件不存在，无法继续索引。", document.FileName);
            parsePath = Path.Combine(Path.GetTempPath(), $"knowledge-rag-generation-{indexGenerationId:N}-{document.Id:N}{Path.GetExtension(document.FileName)}");
            await using (var target = File.Create(parsePath)) await source.CopyToAsync(target, cancellationToken);
            var text = await parser.ExtractTextAsync(parsePath, cancellationToken);
            var chunks = chunker.Split(text);
            if (chunks.Count == 0) throw new InvalidOperationException("没有从文件中提取到可索引文本。");

            var chunkEntities = new List<DocumentChunkEntity>(chunks.Count);
            for (var offset = 0; offset < chunks.Count; offset += _rag.EmbeddingBatchSize)
            {
                if (!await IsGenerationBuildingAsync(indexGenerationId, cancellationToken))
                {
                    await CleanupVectorsAsync(stagedPointIds, document.Id, indexGenerationId);
                    return;
                }

                var batch = chunks.Skip(offset).Take(_rag.EmbeddingBatchSize).ToList();
                var vectors = await embeddings.GenerateBatchAsync(batch, embeddingEndpoint, cancellationToken);
                var points = new List<VectorPoint>(batch.Count);
                for (var i = 0; i < batch.Count; i++)
                {
                    var chunkIndex = offset + i;
                    var chunkId = DocumentIndexVersion.CreatePointId(document.Id, indexGenerationId, chunkIndex);
                    stagedPointIds.Add(chunkId);
                    chunkEntities.Add(new DocumentChunkEntity
                    {
                        Id = chunkId,
                        KnowledgeBaseId = document.KnowledgeBaseId,
                        DocumentId = document.Id,
                        IndexGenerationId = indexGenerationId,
                        ChunkIndex = chunkIndex,
                        Text = batch[i],
                        CharacterCount = batch[i].Length,
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                    points.Add(new VectorPoint(chunkId, document.KnowledgeBaseId, document.Id, document.FileName, chunkIndex, vectors[i], indexGenerationId));
                }
                foreach (var upsertBatch in points.Chunk(_rag.UpsertBatchSize))
                    await vectorStore.UpsertAsync(upsertBatch, cancellationToken);
            }

            await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
            {
                db.ChangeTracker.Clear();
                var currentGeneration = await db.KnowledgeBaseIndexGenerations
                    .SingleOrDefaultAsync(x => x.Id == indexGenerationId, cancellationToken);
                if (currentGeneration is null || currentGeneration.Status != KnowledgeBaseIndexGenerationStatuses.Building)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    if (currentGeneration?.Status != KnowledgeBaseIndexGenerationStatuses.Active)
                        await CleanupVectorsAsync(stagedPointIds, document.Id, indexGenerationId);
                    return;
                }

                var currentDocument = await db.Documents.SingleOrDefaultAsync(x => x.Id == document.Id, cancellationToken);
                var generationDocumentRow = await db.KnowledgeBaseIndexGenerationDocuments
                    .SingleAsync(x => x.IndexGenerationId == indexGenerationId && x.DocumentId == document.Id, cancellationToken);
                if (currentDocument is null || !currentDocument.Sha256.Equals(generationDocumentRow.SourceSha256, StringComparison.OrdinalIgnoreCase))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    await indexGenerationService.FailAsync(indexGenerationId, document.Id, "源文档内容在重建期间发生变化。", cancellationToken);
                    return;
                }
                if (generationDocumentRow.Status == KnowledgeBaseIndexGenerationDocumentStatuses.Ready)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }

                var priorStagingChunks = await db.DocumentChunks
                    .Where(x => x.DocumentId == document.Id && x.IndexGenerationId == indexGenerationId)
                    .ToListAsync(cancellationToken);
                if (priorStagingChunks.Count > 0) db.DocumentChunks.RemoveRange(priorStagingChunks);
                db.DocumentChunks.AddRange(chunkEntities);
                generationDocumentRow.Status = KnowledgeBaseIndexGenerationDocumentStatuses.Ready;
                generationDocumentRow.ChunkCount = chunkEntities.Count;
                generationDocumentRow.Error = null;
                generationDocumentRow.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                currentGeneration.CompletedDocumentCount = await db.KnowledgeBaseIndexGenerationDocuments
                    .CountAsync(x => x.IndexGenerationId == indexGenerationId && x.Status == KnowledgeBaseIndexGenerationDocumentStatuses.Ready, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            await indexGenerationService.TryActivateAsync(indexGenerationId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await indexGenerationService.ResetDocumentToPendingAsync(indexGenerationId, request.DocumentId, CancellationToken.None);
            await CleanupVectorsAsync(stagedPointIds, request.DocumentId, indexGenerationId);
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            var isAlreadyActiveAndReady = await db.KnowledgeBaseIndexGenerations.AsNoTracking()
                .AnyAsync(x => x.Id == indexGenerationId && x.Status == KnowledgeBaseIndexGenerationStatuses.Active, CancellationToken.None) &&
                await db.KnowledgeBaseIndexGenerationDocuments.AsNoTracking()
                    .AnyAsync(x => x.IndexGenerationId == indexGenerationId && x.DocumentId == request.DocumentId &&
                                   x.Status == KnowledgeBaseIndexGenerationDocumentStatuses.Ready, CancellationToken.None);
            if (isAlreadyActiveAndReady) return;

            await indexGenerationService.FailAsync(indexGenerationId, request.DocumentId, ex.Message, CancellationToken.None);
            await CleanupVectorsAsync(stagedPointIds, request.DocumentId, indexGenerationId);
            logger.LogError(ex, "KB index generation {GenerationId} failed while indexing document {DocumentId}", indexGenerationId, request.DocumentId);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(parsePath) && File.Exists(parsePath)) { try { File.Delete(parsePath); } catch { } }
        }
    }

    private Task<bool> IsGenerationBuildingAsync(Guid generationId, CancellationToken cancellationToken)
        => db.KnowledgeBaseIndexGenerations.AsNoTracking()
            .AnyAsync(x => x.Id == generationId && x.Status == KnowledgeBaseIndexGenerationStatuses.Building, cancellationToken);

    private async Task EnsureNotCancelledAsync(DocumentEntity document, CancellationToken cancellationToken)
    {
        await db.Entry(document).ReloadAsync(cancellationToken);
        if (document.Status == DocumentStatus.Cancelled) throw new DocumentProcessingCancelledException();
    }

    private async Task<bool> IsGenerationActiveAsync(Guid documentId, Guid indexGenerationId, IReadOnlyCollection<Guid> stagedPointIds, CancellationToken cancellationToken)
    {
        if (stagedPointIds.Count == 0) return false;
        var activeGenerationId = await db.KnowledgeBases.AsNoTracking()
            .Where(x => db.Documents.Any(d => d.Id == documentId && d.KnowledgeBaseId == x.Id))
            .Select(x => (Guid?)x.ActiveIndexGenerationId)
            .SingleOrDefaultAsync(cancellationToken);
        if (activeGenerationId != indexGenerationId) return false;
        var activeIds = await db.DocumentChunks.AsNoTracking()
            .Where(x => x.DocumentId == documentId && x.IndexGenerationId == indexGenerationId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        return activeIds.Count == stagedPointIds.Count && activeIds.All(stagedPointIds.Contains);
    }

    private async Task CleanupVectorsAsync(IReadOnlyCollection<Guid> pointIds, Guid documentId, Guid indexGenerationId)
    {
        if (pointIds.Count == 0) return;
        try { await vectorStore.DeleteAsync(indexGenerationId, pointIds.ToList(), CancellationToken.None); }
        catch (Exception cleanupEx) { logger.LogWarning(cleanupEx, "Failed to clean vectors for document {DocumentId}, generation {GenerationId}", documentId, indexGenerationId); }
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
        var participatesInBuildingGeneration = await (from generationDocument in db.KnowledgeBaseIndexGenerationDocuments.AsNoTracking()
            join generation in db.KnowledgeBaseIndexGenerations.AsNoTracking() on generationDocument.IndexGenerationId equals generation.Id
            where generationDocument.DocumentId == documentId && generation.Status == KnowledgeBaseIndexGenerationStatuses.Building
            select generationDocument.DocumentId).AnyAsync(cancellationToken);
        if (participatesInBuildingGeneration)
            throw new InvalidOperationException("知识库正在重建索引，此文档参与当前重建，请等待任务结束后再删除。");
        var chunks = await db.DocumentChunks.AsNoTracking().Where(x => x.DocumentId == documentId)
            .Select(x => new { x.Id, x.IndexGenerationId }).ToListAsync(cancellationToken);
        foreach (var generationGroup in chunks.GroupBy(x => x.IndexGenerationId))
            await vectorStore.DeleteAsync(generationGroup.Key, generationGroup.Select(x => x.Id).ToList(), cancellationToken);
        await fileStorage.DeleteAsync(knowledgeBaseId, document.StoredFileName, cancellationToken);
        var knowledgeBase = await db.KnowledgeBases.SingleAsync(x => x.Id == knowledgeBaseId, cancellationToken);
        knowledgeBase.UpdatedAt = DateTimeOffset.UtcNow;
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
