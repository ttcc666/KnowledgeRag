using System.Collections.Concurrent;
using System.Text.Json;
using KnowledgeRag.Infrastructure.Persistence;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Application.Security;
using KnowledgeRag.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeRag.Infrastructure.Conversations;

public sealed class ConversationService(AppDbContext db, KnowledgeBaseAccessService access, RagAgentService agent)
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

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
    {
        if (string.IsNullOrWhiteSpace(message)) throw new InvalidOperationException("消息不能为空。");
        var gate = Locks.GetOrAdd(conversationId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var conversation = await GetOwnedConversationAsync(actor, conversationId, cancellationToken);
            await access.RequireAsync(actor, conversation.KnowledgeBaseId, KnowledgeBaseRole.Viewer, cancellationToken);
            var result = await agent.AskAsync(conversation.KnowledgeBaseId, message, conversation.AgentSessionJson, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            if (conversation.Title == "新会话") conversation.Title = message.Length <= 40 ? message : message[..40] + "…";
            conversation.AgentSessionJson = result.SessionJson;
            conversation.UpdatedAt = now;
            db.ChatMessages.AddRange(
                new ChatMessageEntity { Id = Guid.NewGuid(), ConversationId = conversation.Id, Role = "user", Content = message, CreatedAt = now },
                new ChatMessageEntity { Id = Guid.NewGuid(), ConversationId = conversation.Id, Role = "assistant", Content = result.Answer, SourcesJson = JsonSerializer.Serialize(result.Sources.Select(ToSource)), CreatedAt = now.AddTicks(1) });
            await db.SaveChangesAsync(cancellationToken);
            return new ChatResponse(conversation.Id, result.Answer, result.Sources.Select(ToSource).ToList());
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

    private static ChatSource ToSource(SearchHit hit) => new(hit.DocumentId, hit.FileName, hit.ChunkIndex, hit.SourceLink, hit.Score);
}
