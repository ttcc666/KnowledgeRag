using KnowledgeRag.Application.Abstractions;
using KnowledgeRag.Application.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace KnowledgeRag.Api.Realtime;

public sealed class SignalRDocumentProcessingNotifier(IHubContext<DocumentProcessingHub, IDocumentProcessingClient> hubContext)
    : IDocumentProcessingNotifier
{
    public Task NotifyAsync(DocumentProcessingNotification notification, CancellationToken cancellationToken = default)
        => hubContext.Clients.Group(DocumentProcessingHub.UserGroup(notification.UserId))
            .DocumentProcessingUpdated(notification);
}
