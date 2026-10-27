using Microsoft.AspNetCore.Http;
using KnowledgeRag.Infrastructure.Persistence;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Application.Security;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.Documents;

public sealed class KnowledgeIngestionService(
    AppDbContext db,
    IFileStorage fileStorage,
    DocumentParser parser,
    TextChunker chunker,
    OpenAiCompatibleEmbeddingService embeddings,
    IVectorStore vectorStore,
    IOptions<RagOptions> ragOptions)
{
    private readonly RagOptions _rag = ragOptions.Value;

    public async Task<DocumentEntity> IngestAsync(Guid knowledgeBaseId, Guid userId, IFormFile file, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("rag.ingest");
        activity?.SetTag("rag.knowledge_base_id", knowledgeBaseId.ToString());
        activity?.SetTag("rag.file_name", Path.GetFileName(file.FileName));
        activity?.SetTag("rag.file_size", file.Length);

        if (file.Length == 0) throw new InvalidOperationException("文件为空。");
        if (file.Length > _rag.MaxUploadMegabytes * 1024L * 1024L) throw new InvalidOperationException($"文件超过 {_rag.MaxUploadMegabytes} MB 限制。");
        if (!parser.Supports(file.FileName)) throw new InvalidOperationException("暂不支持该文件格式。支持 txt/md/csv/json/xml/html/pdf/docx。");

        var documentId = Guid.NewGuid();
        var saved = await fileStorage.SaveAsync(knowledgeBaseId, documentId, file, cancellationToken);
        try
        {
            var duplicate = await db.Documents.AsNoTracking().AnyAsync(x => x.KnowledgeBaseId == knowledgeBaseId && x.Sha256 == saved.Sha256 && x.Status == DocumentStatus.Ready, cancellationToken);
            if (duplicate)
            {
                await fileStorage.DeleteAsync(knowledgeBaseId, saved.StorageKey, cancellationToken);
                throw new InvalidOperationException("该知识库中已存在内容相同的文档。");
            }

            var now = DateTimeOffset.UtcNow;
            var document = new DocumentEntity
            {
                Id = documentId, KnowledgeBaseId = knowledgeBaseId, CreatedByUserId = userId, FileName = Path.GetFileName(file.FileName),
                ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                StoredFileName = saved.StorageKey, Sha256 = saved.Sha256, SizeBytes = file.Length, Status = DocumentStatus.Processing, CreatedAt = now, UpdatedAt = now
            };
            db.Documents.Add(document);
            await db.SaveChangesAsync(cancellationToken);

            var pointIds = new List<Guid>();
            try
            {
                var text = await parser.ExtractTextAsync(saved.ParseFilePath, cancellationToken);
                var chunks = chunker.Split(text);
                if (chunks.Count == 0) throw new InvalidOperationException("没有从文件中提取到可索引文本。");
                activity?.SetTag("rag.chunk_count", chunks.Count);

                var chunkEntities = new List<DocumentChunkEntity>(chunks.Count);
                for (var offset = 0; offset < chunks.Count; offset += _rag.EmbeddingBatchSize)
                {
                    var batch = chunks.Skip(offset).Take(_rag.EmbeddingBatchSize).ToList();
                    var vectors = await embeddings.GenerateBatchAsync(batch, cancellationToken);
                    var points = new List<VectorPoint>(batch.Count);
                    for (var i = 0; i < batch.Count; i++)
                    {
                        var chunkId = Guid.NewGuid();
                        pointIds.Add(chunkId);
                        var chunkIndex = offset + i;
                        chunkEntities.Add(new DocumentChunkEntity
                        {
                            Id = chunkId, KnowledgeBaseId = knowledgeBaseId, DocumentId = document.Id, ChunkIndex = chunkIndex,
                            Text = batch[i], CharacterCount = batch[i].Length, CreatedAt = DateTimeOffset.UtcNow
                        });
                        points.Add(new VectorPoint(chunkId, knowledgeBaseId, document.Id, document.FileName, chunkIndex, vectors[i]));
                    }
                    foreach (var upsertBatch in points.Chunk(_rag.UpsertBatchSize)) await vectorStore.UpsertAsync(upsertBatch, cancellationToken);
                }

                db.DocumentChunks.AddRange(chunkEntities);
                document.ChunkCount = chunkEntities.Count;
                document.Status = DocumentStatus.Ready;
                document.LastError = null;
                document.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return document;
            }
            catch (Exception ex)
            {
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
                if (pointIds.Count > 0) { try { await vectorStore.DeleteAsync(pointIds, cancellationToken); } catch { } }
                foreach (var entry in db.ChangeTracker.Entries<DocumentChunkEntity>().Where(x => x.Entity.DocumentId == document.Id).ToList()) entry.State = EntityState.Detached;
                document.Status = DocumentStatus.Failed;
                document.LastError = ex.Message.Length <= 4000 ? ex.Message : ex.Message[..4000];
                document.ChunkCount = 0;
                document.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(CancellationToken.None);
                throw;
            }
        }
        finally
        {
            if (saved.DeleteParseFileWhenDone && File.Exists(saved.ParseFilePath)) File.Delete(saved.ParseFilePath);
        }
    }

    public async Task<IReadOnlyList<DocumentEntity>> ListAsync(Guid knowledgeBaseId, CancellationToken cancellationToken = default)
    {
        var rows = await db.Documents.AsNoTracking()
            .Where(x => x.KnowledgeBaseId == knowledgeBaseId)
            .ToListAsync(cancellationToken);
        return rows.OrderByDescending(x => x.CreatedAt).ToList();
    }

    public async Task<DocumentDownload?> OpenReadAsync(Guid knowledgeBaseId, Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == documentId && x.KnowledgeBaseId == knowledgeBaseId, cancellationToken);
        if (document is null) return null;
        var stream = await fileStorage.OpenReadAsync(knowledgeBaseId, document.StoredFileName, cancellationToken);
        return stream is null ? null : new DocumentDownload(stream, document.ContentType, document.FileName);
    }

    public async Task<bool> DeleteAsync(Guid knowledgeBaseId, Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.SingleOrDefaultAsync(x => x.Id == documentId && x.KnowledgeBaseId == knowledgeBaseId, cancellationToken);
        if (document is null) return false;
        var pointIds = await db.DocumentChunks.AsNoTracking().Where(x => x.DocumentId == documentId).Select(x => x.Id).ToListAsync(cancellationToken);
        await vectorStore.DeleteAsync(pointIds, cancellationToken);
        await fileStorage.DeleteAsync(knowledgeBaseId, document.StoredFileName, cancellationToken);
        db.Documents.Remove(document);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public sealed record DocumentDownload(Stream Stream, string ContentType, string FileName);
