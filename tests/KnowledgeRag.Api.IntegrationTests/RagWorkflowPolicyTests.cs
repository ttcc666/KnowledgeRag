using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Infrastructure.Conversations;
using KnowledgeRag.Infrastructure.Search;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class RagWorkflowPolicyTests
{
    [Fact]
    public void NormalizePlan_keeps_original_query_and_deduplicates_variants()
    {
        var plan = new QueryPlan
        {
            RequiresKnowledge = true,
            Queries = ["审批流程", "审批流程", "  产品审批差异  "]
        };

        var result = RagWorkflowPolicy.NormalizePlan(plan, "A 和 B 的审批流程有什么区别？", 3);

        Assert.Equal(3, result.Queries.Count);
        Assert.Equal("A 和 B 的审批流程有什么区别？", result.Queries[0]);
        Assert.Equal("审批流程", result.Queries[1]);
        Assert.Equal("产品审批差异", result.Queries[2]);
    }

    [Fact]
    public void NormalizePlan_skips_queries_when_knowledge_is_not_required()
    {
        var result = RagWorkflowPolicy.NormalizePlan(new QueryPlan
        {
            RequiresKnowledge = false,
            Queries = ["should disappear"]
        }, "你好", 3);

        Assert.Empty(result.Queries);
    }

    [Fact]
    public void Review_requires_support_confidence_and_no_citation_errors()
    {
        var accepted = new EvidenceReview { Supported = true, Confidence = 0.9 };
        var badCitation = new EvidenceReview { Supported = true, Confidence = 0.9, CitationErrors = ["wrong page"] };

        Assert.True(RagWorkflowPolicy.IsReviewAccepted(accepted, 0.65));
        Assert.False(RagWorkflowPolicy.IsReviewAccepted(badCitation, 0.65));
        Assert.False(RagWorkflowPolicy.IsReviewAccepted(new EvidenceReview { Supported = true, Confidence = 0.4 }, 0.65));
    }

    [Fact]
    public void MergeEvidence_deduplicates_chunks_and_keeps_higher_score()
    {
        var id = Guid.NewGuid();
        var doc = Guid.NewGuid();
        var low = Hit(id, doc, 0.2);
        var high = Hit(id, doc, 0.9);
        var other = Hit(Guid.NewGuid(), doc, 0.5);

        var result = RagWorkflowPolicy.MergeEvidence([low], [high, other], 10);

        Assert.Equal(2, result.Count);
        Assert.Equal(0.9, result[0].Score);
        Assert.Contains(result, x => x.PointId == other.PointId);
    }

    [Fact]
    public void MultiQuery_normalization_is_bounded_and_includes_original_query()
    {
        var result = MultiQuerySearchService.NormalizeQueries(
            "original",
            ["one", "two", "two", "three", "four"],
            maxQueries: 3);

        Assert.Equal(["original", "one", "two"], result);
    }

    private static SearchHit Hit(Guid pointId, Guid documentId, double score)
        => new(pointId, documentId, "doc.md", 0, "evidence", score, "/file");
}
