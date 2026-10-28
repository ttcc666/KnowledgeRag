using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Documents;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class DocumentIndexLifecycleTests
{
    [Fact]
    public void Reindexing_RemainsSearchableAndBusy()
    {
        Assert.True(DocumentIndexLifecycle.IsSearchable(DocumentStatus.Reindexing));
        Assert.True(DocumentIndexLifecycle.IsSearchable(DocumentStatus.Ready));
        Assert.True(DocumentIndexLifecycle.IsBusy(DocumentStatus.Reindexing));
    }

    [Fact]
    public void BlueGreenReindex_TransitionsReadyToReindexingAndFailureRestoresReady()
    {
        Assert.Equal(DocumentStatus.Reindexing, DocumentIndexLifecycle.BeginReindex(DocumentStatus.Ready));
        Assert.Equal(DocumentStatus.Ready, DocumentIndexLifecycle.RestoreSearchableVersionAfterReindexFailure());
        Assert.Throws<InvalidOperationException>(() => DocumentIndexLifecycle.BeginReindex(DocumentStatus.Failed));
    }
}
