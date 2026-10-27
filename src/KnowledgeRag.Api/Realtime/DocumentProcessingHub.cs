using KnowledgeRag.Api.Security;
using KnowledgeRag.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace KnowledgeRag.Api.Realtime;

public interface IDocumentProcessingClient
{
    Task DocumentProcessingUpdated(DocumentProcessingNotification notification);
}

[Authorize]
public sealed class DocumentProcessingHub : Hub<IDocumentProcessingClient>
{
    public override async Task OnConnectedAsync()
    {
        var actor = HttpActorContext.From(Context.User!);
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(actor.UserId), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public static string UserGroup(Guid userId) => $"user:{userId:N}";
}
