using KnowledgeRag.Application.Contracts;

namespace KnowledgeRag.Application.Abstractions;

public interface IDocumentProcessingNotifier
{
    Task NotifyAsync(DocumentProcessingNotification notification, CancellationToken cancellationToken = default);
}
