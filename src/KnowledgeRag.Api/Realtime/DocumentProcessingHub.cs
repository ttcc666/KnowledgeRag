using KnowledgeRag.Api.Security;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.KnowledgeBases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace KnowledgeRag.Api.Realtime;

public interface IDocumentProcessingClient
{
    Task DocumentProcessingUpdated(DocumentProcessingNotification notification);
}

[Authorize]
public sealed class DocumentProcessingHub(KnowledgeBaseAccessService access) : Hub<IDocumentProcessingClient>
{
    public async Task SubscribeKnowledgeBase(Guid knowledgeBaseId)
    {
        var actor = HttpActorContext.From(Context.User!);
        await access.RequireAsync(actor, knowledgeBaseId, KnowledgeBaseRole.Viewer, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(knowledgeBaseId), Context.ConnectionAborted);
    }

    public Task UnsubscribeKnowledgeBase(Guid knowledgeBaseId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(knowledgeBaseId), Context.ConnectionAborted);

    public static string GroupName(Guid knowledgeBaseId) => $"knowledge-base:{knowledgeBaseId:N}";
}
