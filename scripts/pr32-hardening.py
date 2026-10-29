from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected one match, found {count}")
    return text.replace(old, new, 1)


ingestion_path = Path("src/KnowledgeRag.Infrastructure/Documents/KnowledgeIngestionService.cs")
ingestion = ingestion_path.read_text()
ingestion = replace_once(
    ingestion,
    '''            await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
            {
                var knowledgeBase = await db.KnowledgeBases.SingleAsync(x => x.Id == request.KnowledgeBaseId, cancellationToken);
                await db.Entry(knowledgeBase).ReloadAsync(cancellationToken);
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
            }''',
    '''            await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
            {
                var activatedAt = DateTimeOffset.UtcNow;
                var activeGenerationGuard = await db.KnowledgeBases
                    .Where(x => x.Id == request.KnowledgeBaseId && x.ActiveIndexGenerationId == indexGenerationId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UpdatedAt, activatedAt), cancellationToken);
                if (activeGenerationGuard != 1)
                    throw new ActiveIndexGenerationChangedException();

                if (oldChunks.Count > 0) db.DocumentChunks.RemoveRange(oldChunks);
                db.DocumentChunks.AddRange(chunkEntities);
                document.ChunkCount = chunkEntities.Count;
                document.Status = DocumentStatus.Ready;
                document.LastError = null;
                document.UpdatedAt = activatedAt;
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }''',
    "active generation compare-and-touch",
)

ingestion = replace_once(
    ingestion,
    '''    public async Task<bool> DeleteAsync(Guid knowledgeBaseId, Guid documentId, CancellationToken cancellationToken = default)
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
    }''',
    '''    public async Task<bool> DeleteAsync(Guid knowledgeBaseId, Guid documentId, CancellationToken cancellationToken = default)
    {
        DocumentEntity document;
        List<(Guid Id, Guid IndexGenerationId)> chunks;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            document = await db.Documents.SingleOrDefaultAsync(x => x.Id == documentId && x.KnowledgeBaseId == knowledgeBaseId, cancellationToken)
                ?? null!;
            if (document is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
            if (document.Status is DocumentStatus.Queued or DocumentStatus.Processing)
                throw new InvalidOperationException("文档正在后台索引，请先取消任务或等待处理完成。");

            var knowledgeBase = await db.KnowledgeBases.SingleAsync(x => x.Id == knowledgeBaseId, cancellationToken);
            knowledgeBase.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            var participatesInBuildingGeneration = await (from generationDocument in db.KnowledgeBaseIndexGenerationDocuments.AsNoTracking()
                join generation in db.KnowledgeBaseIndexGenerations.AsNoTracking() on generationDocument.IndexGenerationId equals generation.Id
                where generationDocument.DocumentId == documentId && generation.Status == KnowledgeBaseIndexGenerationStatuses.Building
                select generationDocument.DocumentId).AnyAsync(cancellationToken);
            if (participatesInBuildingGeneration)
                throw new InvalidOperationException("知识库正在重建索引，此文档参与当前重建，请等待任务结束后再删除。");

            chunks = await db.DocumentChunks.AsNoTracking().Where(x => x.DocumentId == documentId)
                .Select(x => new ValueTuple<Guid, Guid>(x.Id, x.IndexGenerationId)).ToListAsync(cancellationToken);
            db.Documents.Remove(document);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        foreach (var generationGroup in chunks.GroupBy(x => x.IndexGenerationId))
        {
            try { await vectorStore.DeleteAsync(generationGroup.Key, generationGroup.Select(x => x.Id).ToList(), cancellationToken); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to clean vectors after deleting document {DocumentId}, generation {GenerationId}", documentId, generationGroup.Key); }
        }
        try { await fileStorage.DeleteAsync(knowledgeBaseId, document.StoredFileName, cancellationToken); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to delete stored file after deleting document {DocumentId}", documentId); }
        return true;
    }''',
    "delete generation serialization",
)
ingestion_path.write_text(ingestion)

external_path = Path("tests/KnowledgeRag.Api.IntegrationTests/ExternalBackendTests.cs")
external = external_path.read_text()
external = replace_once(
    external,
    '''        var first = new float[1536]; first[0] = 1;
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
        }''',
    '''        var first = new float[1536]; first[0] = 1;
        var second = new float[1536]; second[1] = 1;
        var firstGenerationId = Guid.NewGuid();
        var secondGenerationId = Guid.NewGuid();
        var firstGenerationPointId = Guid.NewGuid();
        var secondGenerationPointId = Guid.NewGuid();
        var firstGenerationVector = new float[3]; firstGenerationVector[0] = 1;
        var secondGenerationVector = new float[5]; secondGenerationVector[0] = 1;
        try
        {
            await vectorStore.UpsertAsync([
                new VectorPoint(firstId, kbId, documentId, "a.txt", 0, first),
                new VectorPoint(secondId, kbId, documentId, "a.txt", 1, second)
            ]);
            await vectorStore.UpsertAsync([
                new VectorPoint(firstGenerationPointId, kbId, documentId, "a.txt", 0, firstGenerationVector, firstGenerationId)
            ]);
            await vectorStore.UpsertAsync([
                new VectorPoint(secondGenerationPointId, kbId, documentId, "a.txt", 0, secondGenerationVector, secondGenerationId)
            ]);

            var hits = await vectorStore.SearchAsync(first, kbId, 2);
            Assert.NotEmpty(hits);
            Assert.Equal(firstId, hits[0].PointId);
            Assert.True(hits[0].Score > 0.99);

            var firstGenerationHits = await vectorStore.SearchAsync(firstGenerationVector, kbId, firstGenerationId, 2);
            Assert.Single(firstGenerationHits);
            Assert.Equal(firstGenerationPointId, firstGenerationHits[0].PointId);
            var secondGenerationHits = await vectorStore.SearchAsync(secondGenerationVector, kbId, secondGenerationId, 2);
            Assert.Single(secondGenerationHits);
            Assert.Equal(secondGenerationPointId, secondGenerationHits[0].PointId);
        }
        finally
        {
            await vectorStore.DeleteAsync([firstId, secondId]);
            await vectorStore.DeleteGenerationAsync(kbId, firstGenerationId);
            await vectorStore.DeleteGenerationAsync(kbId, secondGenerationId);
        }''',
    "pgvector generation isolation",
)
external = replace_once(
    external,
    '''        var chunk = new DocumentChunkEntity
        {
            Id = Guid.NewGuid(), KnowledgeBaseId = kb.Id, DocumentId = document.Id, ChunkIndex = 0,
            Text = "invoice approval policy requires manager review", CharacterCount = 47, CreatedAt = now
        };
        db.AddRange(user, tenant,
            new TenantMemberEntity { TenantId = tenant.Id, UserId = user.Id, Role = TenantRole.Owner, AddedAt = now }, kb,
            new KnowledgeBaseMemberEntity { KnowledgeBaseId = kb.Id, UserId = user.Id, Role = KnowledgeBaseRole.Owner, AddedAt = now },
            document, chunk);''',
    '''        var chunk = new DocumentChunkEntity
        {
            Id = Guid.NewGuid(), KnowledgeBaseId = kb.Id, DocumentId = document.Id, ChunkIndex = 0,
            Text = "invoice approval policy requires manager review", CharacterCount = 47, CreatedAt = now
        };
        var stagedGenerationId = Guid.NewGuid();
        var stagedChunk = new DocumentChunkEntity
        {
            Id = Guid.NewGuid(), KnowledgeBaseId = kb.Id, DocumentId = document.Id, IndexGenerationId = stagedGenerationId, ChunkIndex = 0,
            Text = "invoice approval staging generation requires director review", CharacterCount = 56, CreatedAt = now.AddSeconds(1)
        };
        db.AddRange(user, tenant,
            new TenantMemberEntity { TenantId = tenant.Id, UserId = user.Id, Role = TenantRole.Owner, AddedAt = now }, kb,
            new KnowledgeBaseMemberEntity { KnowledgeBaseId = kb.Id, UserId = user.Id, Role = KnowledgeBaseRole.Owner, AddedAt = now },
            document, chunk, stagedChunk);''',
    "fts staged generation fixture",
)
external = replace_once(
    external,
    '''            var hit = Assert.Single(results);
            Assert.Equal(chunk.Id, hit.PointId);
            Assert.True(hit.Bm25Score is > 0);''',
    '''            var hit = Assert.Single(results);
            Assert.Equal(chunk.Id, hit.PointId);
            Assert.True(hit.Bm25Score is > 0);

            var stagedResults = await service.SearchAsync(kb.Id, stagedGenerationId, "invoice", 10);
            var stagedHit = Assert.Single(stagedResults);
            Assert.Equal(stagedChunk.Id, stagedHit.PointId);
            Assert.NotEqual(hit.PointId, stagedHit.PointId);''',
    "fts generation isolation assertions",
)
external_path.write_text(external)

changelog_path = Path("CHANGELOG.md")
changelog = changelog_path.read_text()
premature = '''## Unreleased

- 新增知识库级 Embedding 索引代次快照、代次隔离的 pgvector / Qdrant 存储与 BM25/FTS 过滤；重建期间继续查询旧代次，所有文档准备完成且源文档清单未变化后才通过事务切换活动代次。凭据不进入快照，活动 Provider 的 Base URL 变更会被阻止，避免新旧向量空间混用。

'''
if changelog.count("## Unreleased") != 2 or premature not in changelog:
    raise SystemExit("unexpected changelog Unreleased layout")
changelog = changelog.replace(premature, "", 1)
added = "- 新增 **KB-scoped Embedding Index Generation**：按知识库保存不可变 Embedding endpoint/model/dimension 快照，pgvector / Qdrant 以 generation 隔离物理向量空间，BM25 / PostgreSQL FTS 同步按 generation 过滤；重建期间查询继续固定旧 active generation，只有全部文档 staging 完成且源文档 manifest 未变化时才原子切换。\n"
changelog = replace_once(changelog, "### Added\n\n", "### Added\n\n" + added, "changelog Added")
changed = "- 租户默认 Embedding Deployment 现在只作为下一次 KB Reindex 的目标；修改默认值不会直接改变当前检索向量空间。活动/构建中的 generation 会固定 provider/model/dimensions 快照，并阻止破坏该快照的 Provider Base URL / disable 操作；凭据轮换仍允许。\n"
changelog = replace_once(changelog, "### Changed\n\n", "### Changed\n\n" + changed, "changelog Changed")
fixed = "- 修复 KB Embedding Index 状态查询在 SQLite 上直接对 `DateTimeOffset` 排序导致 500；SQLite 改用 `julianday(CreatedAt)` 在数据库内取最新 generation，PostgreSQL 保持原生排序。\n"
changelog = replace_once(changelog, "### Fixed\n\n", "### Fixed\n\n" + fixed, "changelog Fixed")
if changelog.count("## Unreleased") != 1:
    raise SystemExit("changelog must contain exactly one Unreleased section")
changelog_path.write_text(changelog)
