using DotNetCore.CAP;

namespace KnowledgeRag.Infrastructure.Documents;

public sealed class DocumentIndexingSubscriber(KnowledgeIngestionService ingestion) : ICapSubscribe
{
    [CapSubscribe(DocumentEventTopics.IndexRequested)]
    public Task HandleAsync(DocumentIndexRequested request)
        => ingestion.ProcessAsync(request, CancellationToken.None);
}
