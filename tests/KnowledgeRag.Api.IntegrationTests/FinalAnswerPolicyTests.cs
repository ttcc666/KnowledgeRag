using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Infrastructure.Conversations;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class FinalAnswerPolicyTests
{
    [Fact]
    public void Knowledge_answer_with_valid_citation_is_accepted_and_normalized()
    {
        var evidence = new[] { Hit("a.pdf", 2) };

        var result = FinalAnswerPolicy.Validate("结论来自证据 [E1]。", evidence, requiresKnowledge: true);

        Assert.True(result.Accepted);
        Assert.Equal("accepted", result.Status);
        Assert.Equal("结论来自证据 [1]。", result.Answer);
        Assert.Single(result.Sources);
    }

    [Fact]
    public void Invalid_citation_fails_closed_and_exposes_no_source()
    {
        var result = FinalAnswerPolicy.Validate("伪造引用 [E99]。", new[] { Hit("a.pdf", 0) }, requiresKnowledge: true);

        Assert.False(result.Accepted);
        Assert.Equal("invalid-citation", result.Status);
        Assert.Equal(RagWorkflowPolicy.SafeUnsupportedAnswer(), result.Answer);
        Assert.Empty(result.Sources);
    }

    [Fact]
    public void Knowledge_answer_without_citation_fails_closed()
    {
        var result = FinalAnswerPolicy.Validate("这是一个没有引用的知识结论。", new[] { Hit("a.pdf", 0) }, requiresKnowledge: true);

        Assert.False(result.Accepted);
        Assert.Equal("missing-citation", result.Status);
        Assert.Empty(result.Sources);
    }

    [Fact]
    public void Explicit_abstention_is_allowed_without_fabricated_citation()
    {
        var result = FinalAnswerPolicy.Validate("知识库中没有找到足够信息。", [], requiresKnowledge: true);

        Assert.True(result.Accepted);
        Assert.Equal("abstained", result.Status);
        Assert.Empty(result.Sources);
    }

    [Fact]
    public void Non_knowledge_answer_does_not_require_citation()
    {
        var result = FinalAnswerPolicy.Validate("你好！", [], requiresKnowledge: false);

        Assert.True(result.Accepted);
        Assert.Equal("accepted", result.Status);
    }

    private static SearchHit Hit(string fileName, int chunk)
        => new(Guid.NewGuid(), Guid.NewGuid(), fileName, chunk, "evidence", .9, "/source", FusionScore: .03);
}
