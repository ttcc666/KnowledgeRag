using System.Security.Cryptography;
using System.Text;
using DotNetCore.CAP;
using KnowledgeRag.Application.Abstractions;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Application.Security;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.AI;
using KnowledgeRag.Infrastructure.Persistence;
using KnowledgeRag.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeRag.Infrastructure.Documents;

/// <summary>
/// Coordinates a KB-wide blue/green embedding rebuild. Staged chunks and vectors stay invisible
/// until one transaction switches the KB's active generation pointer.
/// </summary>
public sealed class KnowledgeBaseIndexGenerationService(
    AppDbContext db,
    KnowledgeBaseAccessService access,
    TenantAiProviderService tenantAiProviderService,
    IVectorStore vectorStore,
    ICapPublisher capPublisher,
    ILogger<KnowledgeBaseIndexGenerationService> logger)
{
    public async Task<KnowledgeBaseIndexGenerationSummary> GetStatusAsync(
        ActorContext actor,
        Guid knowledgeBaseId,
        CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(actor, knowledgeBaseId, KnowledgeBaseRole.Viewer, cancellationToken);
        var knowledgeBase = await db.KnowledgeBases.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == knowledgeBaseId, cancellationToken)
            ?? throw new KeyNotFoundException("知识库不存在。");

        var activeEndpoint = await tenantAiProviderService.ResolveEmbeddingEndpointForGenerationAsync(
            knowledgeBaseId, knowledgeBase.ActiveIndexGenerationId, cancellationToken);
        var activeGeneration = knowledgeBase.ActiveIndexGenerationId == Guid.Empty
            ? null
            : await db.KnowledgeBaseIndexGenerations.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == knowledgeBase.ActiveIndexGenerationId, cancellationToken);
        var latest = await db.KnowledgeBaseIndexGenerations.AsNoTracking()
            .Where(x => x.KnowledgeBaseId == knowledgeBaseId)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return new KnowledgeBaseIndexGenerationSummary(
            knowledgeBaseId,
            knowledgeBase.ActiveIndexGenerationId,
            activeEndpoint.Endpoint.ProviderType,
            activeEndpoint.Endpoint.Model,
            activeEndpoint.Dimensions,
            activeEndpoint.DeploymentDisplayName,
            activeEndpoint.Endpoint.IsTenantManaged,
            activeGeneration?.ActivatedAt,
            latest?.Id,
            latest?.Status,
            latest?.ProviderType,
            latest?.ModelName,
            latest?.Dimensions,
            latest?.DeploymentDisplayName,
            latest?.EmbeddingDeploymentId.HasValue == true,
            latest?.ExpectedDocumentCount ?? 0,
            latest?.CompletedDocumentCount ?? 0,
            latest?.FailedDocumentCount ?? 0,
            latest?.FailureReason,
            latest?.CreatedAt,
            latest?.Status == KnowledgeBaseIndexGenerationStatuses.Building);
    }

    public async Task<KnowledgeBaseIndexGenerationSummary> StartAsync(
        ActorContext actor,
        Guid knowledgeBaseId,
        CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(actor, knowledgeBaseId, KnowledgeBaseRole.Editor, cancellationToken);
        var target = await tenantAiProviderService.ResolveEmbeddingTargetAsync(knowledgeBaseId, cancellationToken);
        KnowledgeBaseIndexGenerationEntity generation;
        List<DocumentEntity> documents;

        if (IsPostgres())
        {
            using var transaction = db.Database.BeginTransaction(capPublisher, autoCommit: false);
            (generation, documents) = await CreateGenerationStateAsync(actor, knowledgeBaseId, target, cancellationToken);
            foreach (var document in documents)
                await capPublisher.PublishAsync(DocumentEventTopics.IndexRequested,
                    new DocumentIndexRequested(document.Id, knowledgeBaseId, actor.UserId, generation.Id));
            transaction.Commit();
        }
        else
        {
            await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
            {
                (generation, documents) = await CreateGenerationStateAsync(actor, knowledgeBaseId, target, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            try
            {
                foreach (var document in documents)
                    await capPublisher.PublishAsync(DocumentEventTopics.IndexRequested,
                        new DocumentIndexRequested(document.Id, knowledgeBaseId, actor.UserId, generation.Id));
            }
            catch (Exception ex)
            {
                await FailAsync(generation.Id, null, $"无法将索引任务写入队列：{ex.Message}", CancellationToken.None);
                throw;
            }
        }

        if (documents.Count == 0)
            await TryActivateAsync(generation.Id, cancellationToken);

        return await GetStatusAsync(actor, knowledgeBaseId, cancellationToken);
    }

    private async Task<(KnowledgeBaseIndexGenerationEntity Generation, List<DocumentEntity> Documents)> CreateGenerationStateAsync(
        ActorContext actor,
        Guid knowledgeBaseId,
        EmbeddingEndpointResolution target,
        CancellationToken cancellationToken)
    {
        var knowledgeBase = await db.KnowledgeBases.SingleOrDefaultAsync(x => x.Id == knowledgeBaseId, cancellationToken)
            ?? throw new KeyNotFoundException("知识库不存在。");

        // Updating the KB row serializes index activation with uploads, document activation and deletes.
        knowledgeBase.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        if (await db.KnowledgeBaseIndexGenerations.AnyAsync(x =>
            x.KnowledgeBaseId == knowledgeBaseId && x.Status == KnowledgeBaseIndexGenerationStatuses.Building, cancellationToken))
            throw new InvalidOperationException("该知识库已有索引重建任务正在运行，请等待其完成或失败后再重试。");

        if (target.Endpoint.ConnectionId is Guid connectionId)
        {
            var connection = await db.Set<AiProviderConnectionEntity>().AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == connectionId && x.TenantId == knowledgeBase.TenantId, cancellationToken)
                ?? throw new InvalidOperationException("Embedding Provider Connection 不存在。");
            if (!connection.IsEnabled || connection.Revision != target.Endpoint.Revision ||
                !connection.BaseUrl.Equals(target.Endpoint.BaseUrl, StringComparison.Ordinal) ||
                !connection.ProviderType.Equals(target.Endpoint.ProviderType, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Embedding Provider 在开始重建前发生变化，请重新发起索引重建。");
        }

        var documents = await db.Documents.AsNoTracking()
            .Where(x => x.KnowledgeBaseId == knowledgeBaseId && x.ChunkCount > 0)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var generation = new KnowledgeBaseIndexGenerationEntity
        {
            Id = Guid.NewGuid(),
            KnowledgeBaseId = knowledgeBaseId,
            TenantId = knowledgeBase.TenantId,
            EmbeddingDeploymentId = target.Endpoint.DeploymentId,
            DeploymentDisplayName = target.DeploymentDisplayName,
            ProviderConnectionId = target.Endpoint.ConnectionId,
            ProviderType = target.Endpoint.ProviderType,
            BaseUrl = target.Endpoint.BaseUrl,
            ModelName = target.Endpoint.Model,
            Dimensions = target.Dimensions,
            ConnectionRevision = target.Endpoint.Revision,
            DeploymentUpdatedAt = target.DeploymentUpdatedAt,
            Status = KnowledgeBaseIndexGenerationStatuses.Building,
            ExpectedDocumentCount = documents.Count,
            ManifestHash = ComputeManifestHash(documents.Select(x => (x.Id, x.Sha256))),
            CreatedByUserId = actor.UserId,
            CreatedAt = now
        };
        db.KnowledgeBaseIndexGenerations.Add(generation);
        db.KnowledgeBaseIndexGenerationDocuments.AddRange(documents.Select(document =>
            new KnowledgeBaseIndexGenerationDocumentEntity
            {
                IndexGenerationId = generation.Id,
                DocumentId = document.Id,
                SourceSha256 = document.Sha256,
                Status = KnowledgeBaseIndexGenerationDocumentStatuses.Pending,
                CreatedAt = now,
                UpdatedAt = now
            }));
        await db.SaveChangesAsync(cancellationToken);
        return (generation, documents);
    }

    public async Task TryActivateAsync(Guid generationId, CancellationToken cancellationToken = default)
    {
        var identity = await db.KnowledgeBaseIndexGenerations.AsNoTracking()
            .Where(x => x.Id == generationId)
            .Select(x => new { x.KnowledgeBaseId })
            .SingleOrDefaultAsync(cancellationToken);
        if (identity is null) return;

        var knowledgeBaseId = identity.KnowledgeBaseId;
        var previousGenerationId = Guid.Empty;
        string? failureReason = null;
        var activated = false;

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            db.ChangeTracker.Clear();
            var generation = await db.KnowledgeBaseIndexGenerations
                .SingleOrDefaultAsync(x => x.Id == generationId, cancellationToken);
            if (generation is null || generation.Status != KnowledgeBaseIndexGenerationStatuses.Building)
            {
                await transaction.RollbackAsync(cancellationToken);
                return;
            }

            var knowledgeBase = await db.KnowledgeBases
                .SingleOrDefaultAsync(x => x.Id == knowledgeBaseId, cancellationToken);
            if (knowledgeBase is null)
            {
                failureReason = "知识库在索引重建期间已被删除。";
                await transaction.RollbackAsync(cancellationToken);
            }
            else
            {
                // Lock the KB row before checking the manifest. Upload/index/delete paths also touch this row.
                knowledgeBase.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);

                var generationDocuments = await db.KnowledgeBaseIndexGenerationDocuments.AsNoTracking()
                    .Where(x => x.IndexGenerationId == generationId)
                    .OrderBy(x => x.DocumentId)
                    .ToListAsync(cancellationToken);
                if (generationDocuments.Count != generation.ExpectedDocumentCount ||
                    generationDocuments.Any(x => x.Status != KnowledgeBaseIndexGenerationDocumentStatuses.Ready))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }

                var currentDocuments = await db.Documents.AsNoTracking()
                    .Where(x => x.KnowledgeBaseId == knowledgeBaseId && x.ChunkCount > 0)
                    .OrderBy(x => x.Id)
                    .Select(x => new { x.Id, x.Sha256 })
                    .ToListAsync(cancellationToken);
                var currentManifest = ComputeManifestHash(currentDocuments.Select(x => (x.Id, x.Sha256)));
                if (currentDocuments.Count != generation.ExpectedDocumentCount ||
                    !currentManifest.Equals(generation.ManifestHash, StringComparison.Ordinal))
                {
                    failureReason = "重建期间知识库文档集合或内容发生变化；旧索引保持活动状态，请重新发起重建。";
                    await transaction.RollbackAsync(cancellationToken);
                }
                else
                {
                    previousGenerationId = knowledgeBase.ActiveIndexGenerationId;
                    var now = DateTimeOffset.UtcNow;
                    knowledgeBase.ActiveIndexGenerationId = generationId;
                    knowledgeBase.UpdatedAt = now;
                    generation.Status = KnowledgeBaseIndexGenerationStatuses.Active;
                    generation.ActivatedAt = now;
                    generation.CompletedAt = now;

                    if (previousGenerationId != Guid.Empty)
                    {
                        var previous = await db.KnowledgeBaseIndexGenerations
                            .SingleOrDefaultAsync(x => x.Id == previousGenerationId, cancellationToken);
                        if (previous is not null)
                        {
                            previous.Status = KnowledgeBaseIndexGenerationStatuses.Superseded;
                            previous.CompletedAt = now;
                            // Superseded generations retain model metadata but no longer pin a credential connection.
                            previous.ProviderConnectionId = null;
                        }
                    }

                    var chunkCounts = generationDocuments.ToDictionary(x => x.DocumentId, x => x.ChunkCount);
                    var documentIds = chunkCounts.Keys.ToArray();
                    var indexedDocuments = await db.Documents
                        .Where(x => documentIds.Contains(x.Id))
                        .ToListAsync(cancellationToken);
                    foreach (var document in indexedDocuments)
                    {
                        document.ChunkCount = chunkCounts[document.Id];
                        document.Status = DocumentStatus.Ready;
                        document.LastError = null;
                        document.UpdatedAt = now;
                    }

                    await db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    activated = true;
                }
            }
        }

        if (failureReason is not null)
        {
            db.ChangeTracker.Clear();
            await FailAsync(generationId, null, failureReason, cancellationToken);
            return;
        }

        if (!activated) return;
        try
        {
            await db.DocumentChunks
                .Where(x => x.KnowledgeBaseId == knowledgeBaseId && x.IndexGenerationId == previousGenerationId)
                .ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to remove superseded DB chunks for KB {KnowledgeBaseId}, generation {GenerationId}", knowledgeBaseId, previousGenerationId);
        }

        try
        {
            await vectorStore.DeleteGenerationAsync(knowledgeBaseId, previousGenerationId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to remove superseded vector generation for KB {KnowledgeBaseId}, generation {GenerationId}", knowledgeBaseId, previousGenerationId);
        }
    }

    public async Task FailAsync(Guid generationId, Guid? documentId, string error, CancellationToken cancellationToken = default)
    {
        Guid knowledgeBaseId;
        var failure = error.Length <= 2000 ? error : error[..2000];
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            db.ChangeTracker.Clear();
            var generation = await db.KnowledgeBaseIndexGenerations.SingleOrDefaultAsync(x => x.Id == generationId, cancellationToken);
            if (generation is null || generation.Status != KnowledgeBaseIndexGenerationStatuses.Building)
            {
                await transaction.RollbackAsync(cancellationToken);
                return;
            }
            knowledgeBaseId = generation.KnowledgeBaseId;
            var now = DateTimeOffset.UtcNow;
            var documents = await db.KnowledgeBaseIndexGenerationDocuments
                .Where(x => x.IndexGenerationId == generationId)
                .ToListAsync(cancellationToken);
            foreach (var item in documents)
            {
                if (documentId == item.DocumentId || item.Status != KnowledgeBaseIndexGenerationDocumentStatuses.Ready)
                {
                    item.Status = KnowledgeBaseIndexGenerationDocumentStatuses.Failed;
                    item.Error = failure;
                    item.UpdatedAt = now;
                }
            }
            generation.Status = KnowledgeBaseIndexGenerationStatuses.Failed;
            generation.FailureReason = failure;
            generation.FailedDocumentCount = documents.Count(x => x.Status == KnowledgeBaseIndexGenerationDocumentStatuses.Failed);
            generation.CompletedAt = now;
            generation.ProviderConnectionId = null;
            await db.SaveChangesAsync(cancellationToken);
            await db.DocumentChunks
                .Where(x => x.KnowledgeBaseId == knowledgeBaseId && x.IndexGenerationId == generationId)
                .ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        try
        {
            await vectorStore.DeleteGenerationAsync(knowledgeBaseId, generationId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to clean failed vector generation {GenerationId} for KB {KnowledgeBaseId}", generationId, knowledgeBaseId);
        }
    }

    public async Task ResetDocumentToPendingAsync(Guid generationId, Guid documentId, CancellationToken cancellationToken = default)
    {
        var isBuilding = await db.KnowledgeBaseIndexGenerations.AsNoTracking()
            .AnyAsync(x => x.Id == generationId && x.Status == KnowledgeBaseIndexGenerationStatuses.Building, cancellationToken);
        if (!isBuilding) return;
        var now = DateTimeOffset.UtcNow;
        await db.KnowledgeBaseIndexGenerationDocuments
            .Where(x => x.IndexGenerationId == generationId && x.DocumentId == documentId &&
                        x.Status == KnowledgeBaseIndexGenerationDocumentStatuses.Processing)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, KnowledgeBaseIndexGenerationDocumentStatuses.Pending)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken);
    }

    private bool IsPostgres()
        => db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private static string ComputeManifestHash(IEnumerable<(Guid Id, string Sha256)> documents)
    {
        var manifest = string.Join("\n", documents.OrderBy(x => x.Id).Select(x => $"{x.Id:N}:{x.Sha256}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest)));
    }
}
