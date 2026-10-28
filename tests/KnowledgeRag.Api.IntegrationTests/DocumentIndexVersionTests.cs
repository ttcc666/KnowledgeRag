using KnowledgeRag.Infrastructure.Documents;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class DocumentIndexVersionTests
{
    [Fact]
    public void Next_IncrementsMonotonically()
    {
        Assert.Equal(1, DocumentIndexVersion.Next(0));
        Assert.Equal(8, DocumentIndexVersion.Next(7));
    }

    [Fact]
    public void CreatePointId_IsStableForRetry_AndChangesAcrossGenerations()
    {
        var documentId = Guid.Parse("1f852973-e6e5-4574-8ee2-4896e9070bd7");

        var first = DocumentIndexVersion.CreatePointId(documentId, 3, 12);
        var retry = DocumentIndexVersion.CreatePointId(documentId, 3, 12);
        var nextVersion = DocumentIndexVersion.CreatePointId(documentId, 4, 12);
        var nextChunk = DocumentIndexVersion.CreatePointId(documentId, 3, 13);

        Assert.Equal(first, retry);
        Assert.NotEqual(first, nextVersion);
        Assert.NotEqual(first, nextChunk);
    }

    [Theory]
    [InlineData(-1, 1, 0)]
    [InlineData(0, 0, 0)]
    [InlineData(0, 1, -1)]
    public void InvalidGenerationInputs_AreRejected(int currentVersion, int indexVersion, int chunkIndex)
    {
        if (currentVersion < 0)
            Assert.Throws<ArgumentOutOfRangeException>(() => DocumentIndexVersion.Next(currentVersion));
        if (indexVersion <= 0 || chunkIndex < 0)
            Assert.Throws<ArgumentOutOfRangeException>(() => DocumentIndexVersion.CreatePointId(Guid.NewGuid(), indexVersion, chunkIndex));
    }
}
