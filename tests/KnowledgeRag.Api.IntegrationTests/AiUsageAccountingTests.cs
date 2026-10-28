using KnowledgeRag.Infrastructure.Configuration;
using KnowledgeRag.Infrastructure.Conversations;
using KnowledgeRag.Infrastructure.Governance;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class AiUsageAccountingTests
{
    private static readonly ResolvedRagProfile Profile = new(
        EnablePlanner: true,
        PlannerMaxQueries: 3,
        MultiQueryTopK: 6,
        MaxMergedEvidence: 10,
        EnableReviewer: true,
        ReviewerMaxRetries: 1,
        ReviewerMinConfidence: .7,
        PlannerModel: "planner-model",
        AnswerModel: "answer-model",
        ReviewerModel: "reviewer-model",
        AgentMode: "specialists",
        MonthlyBudgetUsd: 10,
        DailyRunLimit: 2,
        PromptProfileId: null,
        PlannerPrompt: null,
        AnswerPrompt: null,
        ReviewerPrompt: null);

    [Fact]
    public void Reservation_CoversPlannerAnswerReviewerEmbeddingAndSpecialist()
    {
        var lines = AiUsageEstimator.BuildReservation(Profile, "测试问题", new AiOptions { EmbeddingModel = "embed-model" }, new RerankerOptions { Mode = "local" });

        Assert.Contains(lines, x => x.Operation == "planner" && x.Model == "planner-model");
        Assert.Contains(lines, x => x.Operation == "answer" && x.Calls == 2 && x.Model == "answer-model");
        Assert.Contains(lines, x => x.Operation == "reviewer" && x.Calls == 2 && x.Model == "reviewer-model");
        Assert.Contains(lines, x => x.Operation == "embedding" && x.Model == "embed-model");
        Assert.Contains(lines, x => x.Operation == "specialist");
        Assert.All(lines, x => Assert.True(x.Estimated));
    }

    [Fact]
    public void ObservedUsage_UsesActualWorkflowEventCounts()
    {
        var trace = new object[]
        {
            Event("planner", "planner", "agent.started", "QueryPlannerAgent"),
            Event("query-1", "retrieval", "query.running", "KnowledgeSearchService", new { query = "alpha" }),
            Event("query-1", "retrieval", "query.completed", "KnowledgeSearchService"),
            Event("retrieval", "retrieval", "stage.completed", "MultiQuerySearchService", new { evidenceCount = 4 }),
            Event("answer", "answer", "agent.started", "KnowledgeAnswerAgent"),
            Event("tool-1", "answer", "tool.started", "ComparisonSpecialistAgent"),
            Event("review-1", "review", "agent.started", "EvidenceReviewerAgent")
        };

        var lines = AiUsageEstimator.BuildObserved(Profile, "alpha", "answer [1]", 1, trace,
            new AiOptions { EmbeddingModel = "embed-model", ChatModel = "chat-model" },
            new RerankerOptions { Mode = "llm", Model = "rerank-model", CandidateCount = 8 });

        Assert.Contains(lines, x => x.Operation == "planner" && x.Calls == 1);
        Assert.Contains(lines, x => x.Operation == "embedding" && x.Calls == 1);
        Assert.Contains(lines, x => x.Operation == "reranker" && x.Calls == 2 && x.Model == "rerank-model");
        Assert.Contains(lines, x => x.Operation == "answer" && x.Calls == 1);
        Assert.Contains(lines, x => x.Operation == "reviewer" && x.Calls == 1);
        Assert.Contains(lines, x => x.Operation == "specialist" && x.Calls == 1);
    }

    [Fact]
    public void BudgetPolicy_CountsReservationsAndReservationCost()
    {
        var now = new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
        var oneActive = new[] { new RagBudgetSnapshot(now.AddMinutes(-5), "reserved", 2m) };
        var dailyProfile = Profile with { DailyRunLimit = 1, MonthlyBudgetUsd = 0 };
        Assert.Throws<InvalidOperationException>(() => RagBudgetPolicy.EnsureAllowed(dailyProfile, oneActive, 0, now));

        var budgetProfile = Profile with { DailyRunLimit = 0, MonthlyBudgetUsd = 3m };
        Assert.Throws<InvalidOperationException>(() => RagBudgetPolicy.EnsureAllowed(budgetProfile, oneActive, 1.1m, now));
    }

    private static RagProgressEvent Event(string stepId, string stage, string type, string actor, object? data = null)
        => new("run", 1, stepId, stage, type, actor, "test", "running", Data: data);
}
