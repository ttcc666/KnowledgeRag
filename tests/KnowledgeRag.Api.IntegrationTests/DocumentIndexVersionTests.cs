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

    [Fact]
    public void CreatePointId_IsStableAndIsolatedAcrossIndexGenerations()
    {
        var documentId = Guid.Parse("1f852973-e6e5-4574-8ee2-4896e9070bd7");
        var secondDocumentId = Guid.Parse("c5c23ed1-72ce-48d5-a4b8-2f02e70b8477");
        var firstGeneration = Guid.Parse("be5c8124-8847-42e9-8a13-9bbd73a7a7e1");
        var secondGeneration = Guid.Parse("a22e6c17-f17d-47a7-8db7-1e5d9b6f14c2");

        var first = DocumentIndexVersion.CreatePointId(documentId, firstGeneration, 3);
        var retry = DocumentIndexVersion.CreatePointId(documentId, firstGeneration, 3);
        var second = DocumentIndexVersion.CreatePointId(documentId, secondGeneration, 3);
        var otherDocument = DocumentIndexVersion.CreatePointId(secondDocumentId, firstGeneration, 3);
        var nextChunk = DocumentIndexVersion.CreatePointId(documentId, firstGeneration, 4);

        Assert.Equal(first, retry);
        Assert.NotEqual(first, second);
        Assert.NotEqual(first, otherDocument);
        Assert.NotEqual(first, nextChunk);
        Assert.Throws<ArgumentException>(() => DocumentIndexVersion.CreatePointId(documentId, Guid.Empty, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentIndexVersion.CreatePointId(documentId, firstGeneration, -1));
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
