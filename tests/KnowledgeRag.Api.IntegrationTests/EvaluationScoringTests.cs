using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Governance;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class EvaluationScoringTests
{
    [Fact]
    public void KnowledgeFact_WithRetrievedEvidenceButNoCitation_FailsGrounding()
    {
        var item = Case("fact", requiresKnowledge: true);
        var assessment = EvaluationScoring.Evaluate(item, "答案来自知识库。", [Hit("policy.pdf")]);

        Assert.False(assessment.CitationPass);
        Assert.False(assessment.GroundingPass);
        Assert.False(assessment.Passed);
        Assert.Equal(1, assessment.RetrievedSourceCount);
        Assert.Empty(assessment.CitedSources);
    }

    [Fact]
    public void KnowledgeFact_WithValidEvidenceCitation_PassesAndNormalizesCitation()
    {
        var item = Case("fact", requiresKnowledge: true, expected: "保留 30 天");
        var assessment = EvaluationScoring.Evaluate(item, "数据保留 30 天 [E1]。", [Hit("policy.pdf")]);

        Assert.True(assessment.CitationPass);
        Assert.True(assessment.GroundingPass);
        Assert.True(assessment.ExpectedPass);
        Assert.True(assessment.Passed);
        Assert.Equal("数据保留 30 天 [1]。", assessment.Answer);
        Assert.Single(assessment.CitedSources);
        Assert.Equal(1, assessment.CitationCount);
        Assert.Equal(0, assessment.InvalidCitationCount);
    }

    [Fact]
    public void KnowledgeFact_WithFabricatedEvidenceId_FailsCitationValidation()
    {
        var item = Case("fact", requiresKnowledge: true);
        var assessment = EvaluationScoring.Evaluate(item, "结论 [E99]。", [Hit("policy.pdf")]);

        Assert.False(assessment.CitationPass);
        Assert.False(assessment.GroundingPass);
        Assert.False(assessment.Passed);
        Assert.Equal(1, assessment.InvalidCitationCount);
        Assert.Empty(assessment.CitedSources);
    }

    [Fact]
    public void NoAnswerCase_PassesOnCleanAbstentionWithoutCitation()
    {
        var item = Case("no-answer", requiresKnowledge: true);
        var assessment = EvaluationScoring.Evaluate(item, "知识库中没有找到足够信息，无法可靠回答。", [Hit("unrelated.pdf")]);

        Assert.True(assessment.InsufficientEvidence);
        Assert.True(assessment.CitationPass);
        Assert.True(assessment.GroundingPass);
        Assert.True(assessment.NoAnswerPass);
        Assert.True(assessment.Passed);
        Assert.Empty(assessment.CitedSources);
    }

    [Fact]
    public void NoAnswerCase_FailsWhenModelAnswersInsteadOfAbstaining()
    {
        var item = Case("no-answer", requiresKnowledge: true);
        var assessment = EvaluationScoring.Evaluate(item, "可以确定答案是 42 [E1]。", [Hit("unrelated.pdf")]);

        Assert.False(assessment.InsufficientEvidence);
        Assert.False(assessment.GroundingPass);
        Assert.False(assessment.NoAnswerPass);
        Assert.False(assessment.Passed);
    }

    private static EvaluationCaseEntity Case(string category, bool requiresKnowledge, string? expected = null)
        => new()
        {
            Id = Guid.NewGuid(),
            DatasetId = Guid.NewGuid(),
            Category = category,
            Query = "测试问题",
            ExpectedAnswerContains = expected,
            RequiresKnowledge = requiresKnowledge,
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static SearchHit Hit(string fileName)
        => new(Guid.NewGuid(), Guid.NewGuid(), fileName, 0, "evidence", .9, "/source");
}
