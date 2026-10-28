using KnowledgeRag.Infrastructure.Documents;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class DocumentIndexVersionTests
{
    [Fact]
    public void CreatePointId_IsStableForRetry_AndChangesAcrossGenerations()
    {
        var documentId = Guid.Parse("1f852973-e6e5-4574-8ee2-4896e9070bd7");
        const long generation = 638973051480000000L;

        var first = DocumentIndexVersion.CreatePointId(documentId, generation, 12);
        var retry = DocumentIndexVersion.CreatePointId(documentId, generation, 12);
        var nextGeneration = DocumentIndexVersion.CreatePointId(documentId, generation + 1, 12);
        var nextChunk = DocumentIndexVersion.CreatePointId(documentId, generation, 13);

        Assert.Equal(first, retry);
        Assert.NotEqual(first, nextGeneration);
        Assert.NotEqual(first, nextChunk);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    public void InvalidGenerationInputs_AreRejected(long generationTicks, int chunkIndex)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DocumentIndexVersion.CreatePointId(Guid.NewGuid(), generationTicks, chunkIndex));
    }
}
