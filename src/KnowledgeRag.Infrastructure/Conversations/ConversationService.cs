using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Application.Security;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Governance;
using KnowledgeRag.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeRag.Infrastructure.Conversations;

public sealed class ConversationService(
    AppDbContext db,
    KnowledgeBaseAccessService access,
    RagAgentService agent,
    RagRunAccountingService accounting,
    RagRuntimeProfileService runtimeProfiles,
    WebhookAutomationService webhooks)
{
    private const string MetadataRole = "__meta";
    private const string ArchiveMarker = "conversation.archived";
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public async Task<ConversationEntity> CreateAsync(ActorContext actor, CreateConversationRequest request, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(actor, request.KnowledgeBaseId, KnowledgeBaseRole.Viewer, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var conversation = new ConversationEntity
        {
            Id = Guid.NewGuid(), KnowledgeBaseId = request.KnowledgeBaseId, UserId = actor.UserId, ApiKeyId = actor.ApiKeyId,
            Title = string.IsNullOrWhiteSpace(request.Title) ? "新会话" : request.Title.Trim(), CreatedAt = now, UpdatedAt = now
        };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(cancellationToken);
        return conversation;
    }

    public async Task<IReadOnlyList<ConversationEntity>> ListAsync(
        ActorContext actor,
        Guid? knowledgeBaseId,
        bool archived = false,
        CancellationToken cancellationToken = default)
    {
        if (knowledgeBaseId.HasValue)
            await access.RequireAsync(actor, knowledgeBaseId.Value, KnowledgeBaseRole.Viewer, cancellationToken);

        if (db.Database.IsSqlite())
        {
            // EF Core SQLite cannot translate DateTimeOffset ORDER BY. Keep the entire filter,
            // archive predicate, ordering and 200-row cap in SQL so a large conversation table is
            // never materialized just to regain provider parity.
            var archiveFlag = archived ? 1 : 0;
            if (actor.IsApiKey)
            {
                var apiKeyId = actor.ApiKeyId!.Value;
                return knowledgeBaseId.HasValue
                    ? await db.Conversations.FromSqlInterpolated($$"""
                        SELECT c.* FROM "Conversations" c
                        WHERE c."UserId" = {{actor.UserId}}
                          AND c."ApiKeyId" = {{apiKeyId}}
                          AND c."KnowledgeBaseId" = {{knowledgeBaseId.Value}}
                          AND (
                            ({{archiveFlag}} = 1 AND EXISTS (
                              SELECT 1 FROM "ChatMessages" m
                              WHERE m."ConversationId" = c."Id" AND m."Role" = {{MetadataRole}} AND m."Content" = {{ArchiveMarker}}
                            ))
                            OR
                            ({{archiveFlag}} = 0 AND NOT EXISTS (
                              SELECT 1 FROM "ChatMessages" m
                              WHERE m."ConversationId" = c."Id" AND m."Role" = {{MetadataRole}} AND m."Content" = {{ArchiveMarker}}
                            ))
                          )
                        ORDER BY julianday(c."UpdatedAt") DESC
                        LIMIT 200
                        """).AsNoTracking().ToListAsync(cancellationToken)
                    : await db.Conversations.FromSqlInterpolated($$"""
                        SELECT c.* FROM "Conversations" c
                        WHERE c."UserId" = {{actor.UserId}}
                          AND c."ApiKeyId" = {{apiKeyId}}
                          AND (
                            ({{archiveFlag}} = 1 AND EXISTS (
                              SELECT 1 FROM "ChatMessages" m
                              WHERE m."ConversationId" = c."Id" AND m."Role" = {{MetadataRole}} AND m."Content" = {{ArchiveMarker}}
                            ))
                            OR
                            ({{archiveFlag}} = 0 AND NOT EXISTS (
                              SELECT 1 FROM "ChatMessages" m
                              WHERE m."ConversationId" = c."Id" AND m."Role" = {{MetadataRole}} AND m."Content" = {{ArchiveMarker}}
                            ))
                          )
                        ORDER BY julianday(c."UpdatedAt") DESC
                        LIMIT 200
                        """).AsNoTracking().ToListAsync(cancellationToken);
            }

            return knowledgeBaseId.HasValue
                ? await db.Conversations.FromSqlInterpolated($$"""
                    SELECT c.* FROM "Conversations" c
                    WHERE c."UserId" = {{actor.UserId}}
                      AND c."ApiKeyId" IS NULL
                      AND c."KnowledgeBaseId" = {{knowledgeBaseId.Value}}
                      AND EXISTS (
                        SELECT 1 FROM "KnowledgeBaseMembers" kbMember
                        WHERE kbMember."KnowledgeBaseId" = c."KnowledgeBaseId"
                          AND kbMember."UserId" = {{actor.UserId}}
                      )
                      AND (
                        ({{archiveFlag}} = 1 AND EXISTS (
                          SELECT 1 FROM "ChatMessages" m
                          WHERE m."ConversationId" = c."Id" AND m."Role" = {{MetadataRole}} AND m."Content" = {{ArchiveMarker}}
                        ))
                        OR
                        ({{archiveFlag}} = 0 AND NOT EXISTS (
                          SELECT 1 FROM "ChatMessages" m
                          WHERE m."ConversationId" = c."Id" AND m."Role" = {{MetadataRole}} AND m."Content" = {{ArchiveMarker}}
                        ))
                      )
                    ORDER BY julianday(c."UpdatedAt") DESC
                    LIMIT 200
                    """).AsNoTracking().ToListAsync(cancellationToken)
                : await db.Conversations.FromSqlInterpolated($$"""
                    SELECT c.* FROM "Conversations" c
                    WHERE c."UserId" = {{actor.UserId}}
                      AND c."ApiKeyId" IS NULL
                      AND EXISTS (
                        SELECT 1 FROM "KnowledgeBaseMembers" kbMember
                        WHERE kbMember."KnowledgeBaseId" = c."KnowledgeBaseId"
                          AND kbMember."UserId" = {{actor.UserId}}
                      )
                      AND (
                        ({{archiveFlag}} = 1 AND EXISTS (
                          SELECT 1 FROM "ChatMessages" m
                          WHERE m."ConversationId" = c."Id" AND m."Role" = {{MetadataRole}} AND m."Content" = {{ArchiveMarker}}
                        ))
                        OR
                        ({{archiveFlag}} = 0 AND NOT EXISTS (
                          SELECT 1 FROM "ChatMessages" m
                          WHERE m."ConversationId" = c."Id" AND m."Role" = {{MetadataRole}} AND m."Content" = {{ArchiveMarker}}
                        ))
                      )
                    ORDER BY julianday(c."UpdatedAt") DESC
                    LIMIT 200
                    """).AsNoTracking().ToListAsync(cancellationToken);
        }

        var query = db.Conversations.AsNoTracking().Where(x => x.UserId == actor.UserId);
        query = actor.IsApiKey ? query.Where(x => x.ApiKeyId == actor.ApiKeyId) : query.Where(x => x.ApiKeyId == null);
        if (knowledgeBaseId.HasValue)
            query = query.Where(x => x.KnowledgeBaseId == knowledgeBaseId.Value);
        if (!actor.IsApiKey)
            query = query.Where(conversation => db.KnowledgeBaseMembers.Any(member =>
                member.KnowledgeBaseId == conversation.KnowledgeBaseId && member.UserId == actor.UserId));
        var archivedIds = db.ChatMessages.AsNoTracking()
            .Where(x => x.Role == MetadataRole && x.Content == ArchiveMarker)
            .Select(x => x.ConversationId);
        query = archived ? query.Where(x => archivedIds.Contains(x.Id)) : query.Where(x => !archivedIds.Contains(x.Id));
        return await query.OrderByDescending(x => x.UpdatedAt).Take(200).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChatMessageEntity>> GetMessagesAsync(ActorContext actor, Guid conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await GetOwnedConversationAsync(actor, conversationId, cancellationToken);
        await access.RequireAsync(actor, conversation.KnowledgeBaseId, KnowledgeBaseRole.Viewer, cancellationToken);
        var rows = await db.ChatMessages.AsNoTracking()
            .Where(x => x.ConversationId == conversationId && x.Role != MetadataRole)
            .ToListAsync(cancellationToken);
        return rows.OrderBy(x => x.CreatedAt)
            .ThenByDescending(x => x.Role)
            .ThenBy(x => x.Id)
            .ToList();
    }

    public async Task<ChatMessagePage> GetMessagePageAsync(
        ActorContext actor,
        Guid conversationId,
        DateTimeOffset? before,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var conversation = await GetOwnedConversationAsync(actor, conversationId, cancellationToken);
        await access.RequireAsync(actor, conversation.KnowledgeBaseId, KnowledgeBaseRole.Viewer, cancellationToken);
        limit = Math.Clamp(limit, 1, 100);
        var take = limit + 1;
        List<ChatMessageEntity> rows;

        if (db.Database.IsSqlite())
        {
            // SQLite EF Core cannot translate DateTimeOffset ordering/comparison. Keep this page
            // bounded in SQL and normalize the stored UTC timestamp with julianday instead of
            // materializing an unbounded conversation into memory. Role and Id are deterministic
            // tie-breakers for legacy rows whose timestamps collapse to the same SQLite precision.
            rows = before.HasValue
                ? await db.ChatMessages.FromSqlInterpolated($$"""
                    SELECT * FROM "ChatMessages"
                    WHERE "ConversationId" = {{conversationId}}
                      AND "Role" <> {{MetadataRole}}
                      AND julianday("CreatedAt") < julianday({{before.Value}})
                    ORDER BY julianday("CreatedAt") DESC, "Role" ASC, "Id" DESC
                    LIMIT {{take}}
                    """).AsNoTracking().ToListAsync(cancellationToken)
                : await db.ChatMessages.FromSqlInterpolated($$"""
                    SELECT * FROM "ChatMessages"
                    WHERE "ConversationId" = {{conversationId}}
                      AND "Role" <> {{MetadataRole}}
                    ORDER BY julianday("CreatedAt") DESC, "Role" ASC, "Id" DESC
                    LIMIT {{take}}
                    """).AsNoTracking().ToListAsync(cancellationToken);
        }
        else
        {
            var query = db.ChatMessages.AsNoTracking()
                .Where(x => x.ConversationId == conversationId && x.Role != MetadataRole);
            if (before.HasValue) query = query.Where(x => x.CreatedAt < before.Value);
            rows = await query
                .OrderByDescending(x => x.CreatedAt)
                .ThenBy(x => x.Role)
                .ThenByDescending(x => x.Id)
                .Take(take)
                .ToListAsync(cancellationToken);
        }

        var hasEarlier = rows.Count > limit;
        if (hasEarlier) rows.RemoveAt(rows.Count - 1);
        rows.Reverse();
        var items = rows.Select(x => new ChatMessageItem(x.Id, x.Role, x.Content, x.SourcesJson, x.CreatedAt)).ToList();
        return new ChatMessagePage(items, hasEarlier, hasEarlier && items.Count > 0 ? items[0].CreatedAt : null);
    }

    public async Task<ConversationEntity> RenameAsync(
        ActorContext actor,
        Guid conversationId,
        string title,
        CancellationToken cancellationToken = default)
    {
        var conversation = await GetOwnedConversationAsync(actor, conversationId, cancellationToken);
        await access.RequireAsync(actor, conversation.KnowledgeBaseId, KnowledgeBaseRole.Viewer, cancellationToken);
        var normalized = title.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) throw new InvalidOperationException("会话标题不能为空。");
        conversation.Title = normalized.Length <= 300 ? normalized : normalized[..300];
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return conversation;
    }

    public async Task SetArchivedAsync(
        ActorContext actor,
        Guid conversationId,
        bool archived,
        CancellationToken cancellationToken = default)
    {
        var conversation = await GetOwnedConversationAsync(actor, conversationId, cancellationToken);
        await access.RequireAsync(actor, conversation.KnowledgeBaseId, KnowledgeBaseRole.Viewer, cancellationToken);
        var markers = await db.ChatMessages
            .Where(x => x.ConversationId == conversationId && x.Role == MetadataRole && x.Content == ArchiveMarker)
            .ToListAsync(cancellationToken);

        if (archived && markers.Count == 0)
        {
            db.ChatMessages.Add(new ChatMessageEntity
            {
                Id = Guid.NewGuid(),
                ConversationId = conversationId,
                Role = MetadataRole,
                Content = ArchiveMarker,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        else if (!archived && markers.Count > 0)
        {
            db.ChatMessages.RemoveRange(markers);
        }

        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveFeedbackAsync(
        ActorContext actor,
        Guid conversationId,
        Guid messageId,
        string rating,
        CancellationToken cancellationToken = default)
    {
        var conversation = await GetOwnedConversationAsync(actor, conversationId, cancellationToken);
        await access.RequireAsync(actor, conversation.KnowledgeBaseId, KnowledgeBaseRole.Viewer, cancellationToken);
        var normalized = rating.Trim().ToLowerInvariant();
        if (normalized is not ("positive" or "negative")) throw new InvalidOperationException("Feedback rating 必须是 positive 或 negative。");
        var exists = await db.ChatMessages.AsNoTracking().AnyAsync(
            x => x.Id == messageId && x.ConversationId == conversationId && x.Role == "assistant",
            cancellationToken);
        if (!exists) throw new KeyNotFoundException("Assistant 消息不存在。");
        var tenantId = await db.KnowledgeBases.AsNoTracking()
            .Where(x => x.Id == conversation.KnowledgeBaseId)
            .Select(x => x.TenantId)
            .SingleAsync(cancellationToken);

        db.AuditLogs.Add(new AuditLogEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = actor.UserId,
            ApiKeyId = actor.ApiKeyId,
            Action = "chat.feedback",
            ResourceType = "ChatMessage",
            ResourceId = messageId.ToString(),
            Summary = $"RAG answer feedback: {normalized}",
            DetailsJson = JsonSerializer.Serialize(new { conversationId, rating = normalized }, WebJson),
            StatusCode = 200,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ChatResponse> SendAsync(ActorContext actor, Guid conversationId, string message, CancellationToken cancellationToken = default)
        => await ExecuteTurnAsync(actor, conversationId, message, null, null, cancellationToken);

    public async Task<ChatResponse> StreamAsync(
        ActorContext actor,
        Guid conversationId,
        string message,
        Func<string, CancellationToken, Task> onDelta,
        Func<RagProgressEvent, CancellationToken, Task>? onProgress = null,
        CancellationToken cancellationToken = default)
        => await ExecuteTurnAsync(actor, conversationId, message, onDelta, onProgress, cancellationToken);

    private async Task<ChatResponse> ExecuteTurnAsync(
        ActorContext actor,
        Guid conversationId,
        string message,
        Func<string, CancellationToken, Task>? onDelta,
        Func<RagProgressEvent, CancellationToken, Task>? onProgress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new InvalidOperationException("消息不能为空。");
        var gate = Locks.GetOrAdd(conversationId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        var timer = Stopwatch.StartNew();
        var trace = new ConcurrentQueue<object>();
        ConversationEntity? conversation = null;
        ResolvedRagProfile? profile = null;
        RagRunEntity? reservation = null;
        var settled = false;
        var requiresKnowledge = true;
        try
        {
            conversation = await GetOwnedConversationAsync(actor, conversationId, cancellationToken);
            await access.RequireAsync(actor, conversation.KnowledgeBaseId, KnowledgeBaseRole.Viewer, cancellationToken);
            profile = await runtimeProfiles.ResolveAsync(conversation.KnowledgeBaseId, cancellationToken);
            reservation = await accounting.ReserveAsync(actor, conversation.KnowledgeBaseId, conversation.Id, message, profile, cancellationToken);

            async Task Progress(RagProgressEvent progress, CancellationToken token)
            {
                trace.Enqueue(progress);
                if (TryReadRequiresKnowledge(progress, out var value)) requiresKnowledge = value;
                if (onProgress is not null) await onProgress(progress, token);
            }

            // Never release Agent text directly. The final answer is buffered until the workflow has
            // completed and the deterministic Citation Gate has validated/normalized it.
            var result = onDelta is null
                ? await agent.AskAsync(conversation.KnowledgeBaseId, message, conversation.AgentSessionJson, Progress, cancellationToken)
                : await agent.StreamAsync(
                    conversation.KnowledgeBaseId,
                    message,
                    conversation.AgentSessionJson,
                    static (_, _) => Task.CompletedTask,
                    Progress,
                    cancellationToken);

            var final = FinalAnswerPolicy.Validate(result.Answer, result.Sources, requiresKnowledge);
            trace.Enqueue(new
            {
                type = "citation.validation",
                accepted = final.Accepted,
                gateStatus = final.Status,
                requiresKnowledge,
                citationCount = final.CitationCount,
                invalidCitationCount = final.InvalidCitationCount,
                hadCitationMarkers = final.HadCitationMarkers,
                retrievedEvidenceCount = result.Sources.Count,
                citedSourceCount = final.Sources.Count
            });

            var now = DateTimeOffset.UtcNow;
            if (conversation.Title == "新会话") conversation.Title = message.Length <= 40 ? message : message[..40] + "…";
            // Never persist a rejected or abstaining draft session. A future repair/turn must continue
            // from the last accepted session snapshot, not from unverified model state.
            if (final.Accepted && !FinalAnswerPolicy.IsInsufficientEvidence(final.Answer))
                conversation.AgentSessionJson = result.SessionJson;
            conversation.UpdatedAt = now;
            var sources = final.Sources.Select(ToSource).ToList();
            db.ChatMessages.AddRange(
                new ChatMessageEntity { Id = Guid.NewGuid(), ConversationId = conversation.Id, Role = "user", Content = message, CreatedAt = now },
                // Keep the turn distinct at SQLite/JavaScript millisecond precision. Older rows are
                // still protected by deterministic Role/Id tie-breakers in the read path.
                new ChatMessageEntity { Id = Guid.NewGuid(), ConversationId = conversation.Id, Role = "assistant", Content = final.Answer, SourcesJson = JsonSerializer.Serialize(sources, WebJson), CreatedAt = now.AddMilliseconds(1) });
            await db.SaveChangesAsync(cancellationToken);

            timer.Stop();
            var run = await accounting.SettleAsync(reservation.Id, profile, message, final.Answer, sources.Count,
                timer.ElapsedMilliseconds, trace.ToArray(), "completed", cancellationToken);
            settled = true;
            await webhooks.DispatchAsync(run.TenantId, "rag.run.completed", new
            {
                run.Id, run.KnowledgeBaseId, run.ConversationId, run.Status, run.SourceCount, run.LatencyMs,
                run.EstimatedInputTokens, run.EstimatedOutputTokens, run.EstimatedCostUsd, run.AnswerModel, run.AgentMode
            }, cancellationToken);

            if (onDelta is not null && !string.IsNullOrEmpty(final.Answer))
                await onDelta(final.Answer, cancellationToken);
            return new ChatResponse(conversation.Id, final.Answer, sources);
        }
        catch (OperationCanceledException)
        {
            if (!settled && reservation is not null && profile is not null)
                await accounting.SettleAsync(reservation.Id, profile, message, string.Empty, 0,
                    timer.ElapsedMilliseconds, trace.ToArray(), "cancelled", CancellationToken.None);
            throw;
        }
        catch
        {
            if (!settled && reservation is not null && profile is not null)
                await accounting.SettleAsync(reservation.Id, profile, message, string.Empty, 0,
                    timer.ElapsedMilliseconds, trace.ToArray(), "failed", CancellationToken.None);
            throw;
        }
        finally { gate.Release(); }
    }

    public async Task DeleteAsync(ActorContext actor, Guid conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await GetOwnedConversationAsync(actor, conversationId, cancellationToken);
        db.Conversations.Remove(conversation);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<ConversationEntity> GetOwnedConversationAsync(ActorContext actor, Guid conversationId, CancellationToken ct)
    {
        var query = db.Conversations.Where(x => x.Id == conversationId && x.UserId == actor.UserId);
        query = actor.IsApiKey ? query.Where(x => x.ApiKeyId == actor.ApiKeyId) : query.Where(x => x.ApiKeyId == null);
        return await query.SingleOrDefaultAsync(ct) ?? throw new KeyNotFoundException("会话不存在。");
    }

    private static bool TryReadRequiresKnowledge(RagProgressEvent progress, out bool value)
    {
        value = true;
        if (progress.Stage != "planner" || progress.Data is null) return false;
        try
        {
            var element = JsonSerializer.SerializeToElement(progress.Data, WebJson);
            if (!element.TryGetProperty("requiresKnowledge", out var property) ||
                property.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            value = property.GetBoolean();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ChatSource ToSource(SearchHit hit)
        => new(hit.DocumentId, hit.FileName, hit.ChunkIndex, hit.SourceLink, hit.Score,
            hit.Text.Length <= 1400 ? hit.Text : hit.Text[..1400] + "…", hit.PageNumber,
            hit.VectorScore, hit.Bm25Score, hit.FusionScore, hit.RerankScore);
}
