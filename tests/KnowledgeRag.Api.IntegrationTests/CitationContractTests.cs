using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Infrastructure.Conversations;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class CitationContractTests
{
    [Fact]
    public void Normalize_MapsOnlyActuallyCitedEvidence_InFirstAppearanceOrder()
    {
        var evidence = new[]
        {
            Hit(1, "a.pdf"),
            Hit(2, "b.pdf"),
            Hit(3, "c.pdf")
        };

        var result = CitationContract.Normalize("第二份文档支持结论 [E2]，第一份补充说明 [E1]，再次引用 [E2]。", evidence);

        Assert.Equal("第二份文档支持结论 [1]，第一份补充说明 [2]，再次引用 [1]。", result.Answer);
        Assert.Equal(2, result.CitationCount);
        Assert.Equal(0, result.InvalidCitationCount);
        Assert.Equal(new[] { "b.pdf", "a.pdf" }, result.Sources.Select(x => x.FileName));
    }

    [Fact]
    public void Normalize_RemovesInvalidEvidenceReferences_AndDoesNotExposeThemAsSources()
    {
        var evidence = new[] { Hit(1, "a.pdf") };

        var result = CitationContract.Normalize("可验证事实 [E1]，伪造来源 [E99]。", evidence);

        Assert.Equal(1, result.CitationCount);
        Assert.Equal(1, result.InvalidCitationCount);
        Assert.Single(result.Sources);
        Assert.DoesNotContain("E99", result.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_NoCitationMarkers_DoesNotPretendRetrievedEvidenceWasCited()
    {
        var result = CitationContract.Normalize("没有引用的回答。", new[] { Hit(1, "a.pdf") });

        Assert.False(result.HadCitationMarkers);
        Assert.Empty(result.Sources);
        Assert.Equal("没有引用的回答。", result.Answer);
    }

    private static SearchHit Hit(int chunkIndex, string fileName)
        => new(Guid.NewGuid(), Guid.NewGuid(), fileName, chunkIndex, "evidence", .9, "/source");
}
