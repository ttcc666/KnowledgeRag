using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Governance;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class EvaluationRetrievalScoringTests
{
    [Fact]
    public void Retrieval_metrics_score_expected_sources_by_rank()
    {
        var expected = new[]
        {
            new EvaluationExpectedSource("a.pdf", 2),
            new EvaluationExpectedSource("b.pdf")
        };
        var evidence = new[]
        {
            Hit("b.pdf", 7),
            Hit("noise.pdf", 0),
            Hit("a.pdf", 2)
        };

        var result = EvaluationScoring.EvaluateRetrieval(expected, evidence);

        Assert.True(result.Applicable);
        Assert.True(result.Passed);
        Assert.Equal(2, result.MatchedSourceCount);
        Assert.Equal(1d, result.RecallAtK, 6);
        Assert.Equal(1d, result.Mrr, 6);
        Assert.InRange(result.Ndcg, 0.8, 1.0);
    }

    [Fact]
    public void Retrieval_metrics_fail_when_no_expected_source_is_retrieved()
    {
        var result = EvaluationScoring.EvaluateRetrieval(
            [new EvaluationExpectedSource("expected.pdf")],
            [Hit("other.pdf", 0)]);

        Assert.True(result.Applicable);
        Assert.False(result.Passed);
        Assert.False(result.Hit);
        Assert.Equal(0, result.RecallAtK);
        Assert.Equal(0, result.Mrr);
        Assert.Equal(0, result.Ndcg);
    }

    [Fact]
    public void Expectation_codec_keeps_legacy_text_and_round_trips_v3_sources()
    {
        var legacy = EvaluationExpectationCodec.Parse("must contain");
        Assert.Equal("must contain", legacy.AnswerContains);
        Assert.Empty(legacy.Sources);

        var stored = EvaluationExpectationCodec.Serialize(
            "answer fragment",
            [new EvaluationExpectedSource(" policy.pdf ", 4), new EvaluationExpectedSource("policy.pdf", 4)]);
        var decoded = EvaluationExpectationCodec.Parse(stored);

        Assert.Equal("answer fragment", decoded.AnswerContains);
        Assert.Single(decoded.Sources);
        Assert.Equal("policy.pdf", decoded.Sources[0].FileName);
        Assert.Equal(4, decoded.Sources[0].ChunkIndex);
    }

    [Fact]
    public void Case_with_expected_source_fails_when_retrieval_misses_even_if_answer_format_passes()
    {
        var entity = new EvaluationCaseEntity
        {
            Id = Guid.NewGuid(),
            DatasetId = Guid.NewGuid(),
            Category = "fact",
            Query = "q",
            ExpectedAnswerContains = EvaluationExpectationCodec.Serialize(
                "结论",
                [new EvaluationExpectedSource("expected.pdf")]),
            RequiresKnowledge = true
        };
        var evidence = new[] { Hit("other.pdf", 0) };

        var assessment = EvaluationScoring.Evaluate(entity, "结论 [E1]", evidence);

        Assert.True(assessment.ExpectedPass);
        Assert.True(assessment.CitationPass);
        Assert.False(assessment.Retrieval.Passed);
        Assert.False(assessment.Passed);
    }

    private static SearchHit Hit(string fileName, int chunk)
        => new(Guid.NewGuid(), Guid.NewGuid(), fileName, chunk, "evidence", .9, "/source", FusionScore: .03);
}
