using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using KnowledgeRag.Infrastructure.Persistence;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Application.Security;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Governance;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeRag.Infrastructure.Conversations;

public sealed class ConversationService(
    AppDbContext db,
    KnowledgeBaseAccessService access,
    RagAgentService agent,
    GovernanceService governance,
    RagRuntimeProfileService runtimeProfiles,
    WebhookAutomationService webhooks)
{
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

    public async Task<IReadOnlyList<ConversationEntity>> ListAsync(ActorContext actor, Guid? knowledgeBaseId, CancellationToken cancellationToken = default)
    {
        var query = db.Conversations.AsNoTracking().Where(x => x.UserId == actor.UserId);
        query = actor.IsApiKey ? query.Where(x => x.ApiKeyId == actor.ApiKeyId) : query.Where(x => x.ApiKeyId == null);
        if (knowledgeBaseId.HasValue)
        {
            await access.RequireAsync(actor, knowledgeBaseId.Value, KnowledgeBaseRole.Viewer, cancellationToken);
            query = query.Where(x => x.KnowledgeBaseId == knowledgeBaseId.Value);
        }
        var rows = await query.ToListAsync(cancellationToken);
        return rows.OrderByDescending(x => x.UpdatedAt).Take(200).ToList();
    }

    public async Task<IReadOnlyList<ChatMessageEntity>> GetMessagesAsync(ActorContext actor, Guid conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await GetOwnedConversationAsync(actor, conversationId, cancellationToken);
        await access.RequireAsync(actor, conversation.KnowledgeBaseId, KnowledgeBaseRole.Viewer, cancellationToken);
        var rows = await db.ChatMessages.AsNoTracking().Where(x => x.ConversationId == conversationId).ToListAsync(cancellationToken);
        return rows.OrderBy(x => x.CreatedAt).ToList();
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
        try
        {
            conversation = await GetOwnedConversationAsync(actor, conversationId, cancellationToken);
            await access.RequireAsync(actor, conversation.KnowledgeBaseId, KnowledgeBaseRole.Viewer, cancellationToken);
            var profile = await runtimeProfiles.ResolveAsync(conversation.KnowledgeBaseId, cancellationToken);
            await EnforceBudgetAsync(actor, conversation.KnowledgeBaseId, profile, cancellationToken);

            async Task Progress(RagProgressEvent progress, CancellationToken token)
            {
                trace.Enqueue(progress);
                if (onProgress is not null) await onProgress(progress, token);
            }

            var result = onDelta is null
                ? await agent.AskAsync(conversation.KnowledgeBaseId, message, conversation.AgentSessionJson, Progress, cancellationToken)
                : await agent.StreamAsync(conversation.KnowledgeBaseId, message, conversation.AgentSessionJson, onDelta, Progress, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            if (conversation.Title == "新会话") conversation.Title = message.Length <= 40 ? message : message[..40] + "…";
            conversation.AgentSessionJson = result.SessionJson;
            conversation.UpdatedAt = now;
            var sources = result.Sources.Select(ToSource).ToList();
            db.ChatMessages.AddRange(
                new ChatMessageEntity { Id = Guid.NewGuid(), ConversationId = conversation.Id, Role = "user", Content = message, CreatedAt = now },
                new ChatMessageEntity { Id = Guid.NewGuid(), ConversationId = conversation.Id, Role = "assistant", Content = result.Answer, SourcesJson = JsonSerializer.Serialize(sources, WebJson), CreatedAt = now.AddTicks(1) });
            await db.SaveChangesAsync(cancellationToken);

            timer.Stop();
            var run = await governance.RecordRunAsync(actor, conversation.KnowledgeBaseId, conversation.Id, message, result.Answer, sources.Count,
                profile.AnswerModel, profile.AgentMode, timer.ElapsedMilliseconds, trace.ToArray(), "completed", cancellationToken);
            await webhooks.DispatchAsync(run.TenantId, "rag.run.completed", new
            {
                run.Id, run.KnowledgeBaseId, run.ConversationId, run.Status, run.SourceCount, run.LatencyMs,
                run.EstimatedInputTokens, run.EstimatedOutputTokens, run.EstimatedCostUsd, run.AnswerModel, run.AgentMode
            }, cancellationToken);
            return new ChatResponse(conversation.Id, result.Answer, sources);
        }
        catch (OperationCanceledException)
        {
            if (conversation is not null)
            {
                var profile = await runtimeProfiles.ResolveAsync(conversation.KnowledgeBaseId, CancellationToken.None);
                await governance.RecordRunAsync(actor, conversation.KnowledgeBaseId, conversation.Id, message, string.Empty, 0,
                    profile.AnswerModel, profile.AgentMode, timer.ElapsedMilliseconds, trace.ToArray(), "cancelled", CancellationToken.None);
            }
            throw;
        }
        catch
        {
            if (conversation is not null)
            {
                var profile = await runtimeProfiles.ResolveAsync(conversation.KnowledgeBaseId, CancellationToken.None);
                await governance.RecordRunAsync(actor, conversation.KnowledgeBaseId, conversation.Id, message, string.Empty, 0,
                    profile.AnswerModel, profile.AgentMode, timer.ElapsedMilliseconds, trace.ToArray(), "failed", CancellationToken.None);
            }
            throw;
        }
        finally { gate.Release(); }
    }

    private async Task EnforceBudgetAsync(ActorContext actor, Guid knowledgeBaseId, ResolvedRagProfile profile, CancellationToken ct)
    {
        if (profile.DailyRunLimit <= 0 && profile.MonthlyBudgetUsd <= 0) return;
        var tenantId = await governance.ResolveTenantIdAsync(knowledgeBaseId, ct);
        var runs = db.Set<RagRunEntity>().AsNoTracking().Where(x => x.KnowledgeBaseId == knowledgeBaseId && x.TenantId == tenantId && x.Status == "completed");
        if (profile.DailyRunLimit > 0)
        {
            var start = DateTimeOffset.UtcNow.Date;
            var today = await runs.CountAsync(x => x.CreatedAt >= start, ct);
            if (today >= profile.DailyRunLimit) throw new InvalidOperationException("该知识库已达到今日 RAG Run 配额。");
        }
        if (profile.MonthlyBudgetUsd > 0)
        {
            var now = DateTimeOffset.UtcNow;
            var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
            var spend = await runs.Where(x => x.CreatedAt >= start).SumAsync(x => x.EstimatedCostUsd, ct);
            if (spend >= profile.MonthlyBudgetUsd) throw new InvalidOperationException("该知识库已达到本月 AI 成本预算。");
        }
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

    private static ChatSource ToSource(SearchHit hit)
        => new(hit.DocumentId, hit.FileName, hit.ChunkIndex, hit.SourceLink, hit.Score,
            hit.Text.Length <= 1400 ? hit.Text : hit.Text[..1400] + "…", hit.PageNumber,
            hit.VectorScore, hit.Bm25Score, hit.FusionScore, hit.RerankScore);
}
