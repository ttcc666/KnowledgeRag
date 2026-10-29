using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.AI;
using KnowledgeRag.Infrastructure.Configuration;
using KnowledgeRag.Infrastructure.Conversations;

namespace KnowledgeRag.Infrastructure.Governance;

public sealed record AiUsageEstimate(
    string Operation,
    string Model,
    int Calls,
    int InputTokens,
    int OutputTokens,
    bool Estimated = true,
    Guid? DeploymentId = null,
    string? ProviderType = null);

public sealed record AiUsageLineItem(
    string Operation,
    string Model,
    int Calls,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    bool Estimated = true,
    Guid? DeploymentId = null,
    string? ProviderType = null);

public sealed record RagBudgetSnapshot(DateTimeOffset CreatedAt, string Status, decimal CostUsd);

public static class RagBudgetPolicy
{
    public static void EnsureAllowed(
        ResolvedRagProfile profile,
        IReadOnlyList<RagBudgetSnapshot> runs,
        decimal reservationCostUsd,
        DateTimeOffset now)
    {
        if (profile.DailyRunLimit > 0)
        {
            var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
            var activeToday = runs.Count(x =>
                x.CreatedAt >= dayStart &&
                (string.Equals(x.Status, "completed", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(x.Status, "reserved", StringComparison.OrdinalIgnoreCase)));
            if (activeToday >= profile.DailyRunLimit)
                throw new InvalidOperationException("该知识库已达到今日 RAG Run 配额。");
        }

        if (profile.MonthlyBudgetUsd > 0)
        {
            var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
            var spend = runs.Where(x => x.CreatedAt >= monthStart).Sum(x => x.CostUsd);
            if (spend >= profile.MonthlyBudgetUsd || spend + reservationCostUsd > profile.MonthlyBudgetUsd)
                throw new InvalidOperationException("该知识库已达到本月 AI 成本预算，或剩余额度不足以预留本次运行。");
        }
    }
}

public static class AiUsageEstimator
{
    public static IReadOnlyList<AiUsageEstimate> BuildReservation(
        ResolvedRagProfile profile,
        string query,
        AiOptions ai,
        RerankerOptions reranker)
    {
        var result = new List<AiUsageEstimate>();
        var queryTokens = EstimateTokens(query);
        var evidenceTokens = Math.Max(1, profile.MaxMergedEvidence) * 450;
        var retrievalRounds = 1 + (profile.EnableReviewer ? profile.ReviewerMaxRetries : 0);
        var queriesPerRound = profile.EnablePlanner ? Math.Max(1, profile.PlannerMaxQueries) : 1;
        var queryCalls = retrievalRounds * queriesPerRound;

        if (profile.EnablePlanner)
            result.Add(ForEndpoint("planner", CompatEndpoint(profile.PlannerEndpoint, profile.PlannerModel), 1, queryTokens + 192, 192));

        var embeddingEndpoint = profile.EmbeddingEndpoint?.Endpoint;
        result.Add(embeddingEndpoint is not null
            ? ForEndpoint("embedding", embeddingEndpoint, queryCalls, queryCalls * Math.Max(32, queryTokens + 32), 0)
            : new AiUsageEstimate("embedding", ai.EmbeddingModel, queryCalls,
                queryCalls * Math.Max(32, queryTokens + 32), 0, DeploymentId: null, ProviderType: AiProviderTypes.OpenAiCompatible));

        if (string.Equals(reranker.Mode, "llm", StringComparison.OrdinalIgnoreCase))
        {
            var rerankCalls = queryCalls + retrievalRounds;
            var candidateTokens = Math.Max(1, reranker.CandidateCount) * Math.Max(64, reranker.MaxDocumentCharacters / 4);
            if (profile.RerankerEndpoint is { } routedReranker)
                result.Add(ForEndpoint("reranker", routedReranker, rerankCalls,
                    rerankCalls * (queryTokens + candidateTokens), rerankCalls * Math.Max(64, reranker.CandidateCount * 12)));
            else
            {
                var rerankModel = string.IsNullOrWhiteSpace(reranker.Model) ? ai.ChatModel : reranker.Model;
                result.Add(new AiUsageEstimate("reranker", rerankModel, rerankCalls,
                    rerankCalls * (queryTokens + candidateTokens), rerankCalls * Math.Max(64, reranker.CandidateCount * 12),
                    DeploymentId: null, ProviderType: AiProviderTypes.OpenAiCompatible));
            }
        }

        var answerCalls = 1 + (profile.EnableReviewer ? profile.ReviewerMaxRetries : 0);
        result.Add(ForEndpoint("answer", CompatEndpoint(profile.AnswerEndpoint, profile.AnswerModel), answerCalls,
            answerCalls * (queryTokens + evidenceTokens + 256), answerCalls * 1200));

        if (profile.EnableReviewer)
        {
            var reviewCalls = profile.ReviewerMaxRetries + 1;
            result.Add(ForEndpoint("reviewer", CompatEndpoint(profile.ReviewerEndpoint, profile.ReviewerModel), reviewCalls,
                reviewCalls * (queryTokens + evidenceTokens + 1200), reviewCalls * 320));
        }

        if (!string.Equals(profile.AgentMode, "single", StringComparison.OrdinalIgnoreCase))
            result.Add(ForEndpoint("specialist", CompatEndpoint(profile.ReviewerEndpoint, profile.ReviewerModel), 1,
                queryTokens + evidenceTokens + 256, 640));

        return result;
    }

    public static IReadOnlyList<AiUsageEstimate> BuildObserved(
        ResolvedRagProfile profile,
        string query,
        string answer,
        int citedSourceCount,
        IReadOnlyList<object> trace,
        AiOptions ai,
        RerankerOptions reranker)
    {
        var events = trace.OfType<RagProgressEvent>().ToArray();
        var result = new List<AiUsageEstimate>();
        var queryTokens = EstimateTokens(query);
        var evidenceCount = Math.Max(citedSourceCount, events
            .SelectMany(x => new[] { ReadInt(x.Data, "evidenceCount"), ReadInt(x.Data, "mergedEvidenceCount") })
            .DefaultIfEmpty(0)
            .Max());
        var evidenceTokens = Math.Max(0, evidenceCount) * 450;

        var plannerCalls = CountStarted(events, "QueryPlannerAgent", "agent.started");
        if (plannerCalls > 0)
            result.Add(ForEndpoint("planner", CompatEndpoint(profile.PlannerEndpoint, profile.PlannerModel), plannerCalls,
                plannerCalls * (queryTokens + 192), plannerCalls * 192));

        var queryEvents = events.Where(x =>
            string.Equals(x.Stage, "retrieval", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Type, "query.running", StringComparison.OrdinalIgnoreCase)).ToArray();
        var queryInputTokens = queryEvents.Sum(x => Math.Max(1, EstimateTokens(ReadString(x.Data, "query") ?? query)));
        if (queryEvents.Length > 0)
        {
            var embeddingEndpoint = profile.EmbeddingEndpoint?.Endpoint;
            result.Add(embeddingEndpoint is not null
                ? ForEndpoint("embedding", embeddingEndpoint, queryEvents.Length, queryInputTokens, 0)
                : new AiUsageEstimate("embedding", ai.EmbeddingModel, queryEvents.Length, queryInputTokens, 0,
                    DeploymentId: null, ProviderType: AiProviderTypes.OpenAiCompatible));
        }

        if (string.Equals(reranker.Mode, "llm", StringComparison.OrdinalIgnoreCase))
        {
            var completedQueries = events.Count(x => string.Equals(x.Type, "query.completed", StringComparison.OrdinalIgnoreCase));
            var completedRounds = events.Count(x =>
                string.Equals(x.Stage, "retrieval", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Type, "stage.completed", StringComparison.OrdinalIgnoreCase));
            var rerankCalls = completedQueries + completedRounds;
            if (rerankCalls > 0)
            {
                var candidateTokens = Math.Max(1, reranker.CandidateCount) * Math.Max(64, reranker.MaxDocumentCharacters / 4);
                if (profile.RerankerEndpoint is { } routedReranker)
                    result.Add(ForEndpoint("reranker", routedReranker, rerankCalls,
                        rerankCalls * (queryTokens + candidateTokens), rerankCalls * Math.Max(64, reranker.CandidateCount * 12)));
                else
                {
                    var rerankModel = string.IsNullOrWhiteSpace(reranker.Model) ? ai.ChatModel : reranker.Model;
                    result.Add(new AiUsageEstimate("reranker", rerankModel, rerankCalls,
                        rerankCalls * (queryTokens + candidateTokens), rerankCalls * Math.Max(64, reranker.CandidateCount * 12),
                        DeploymentId: null, ProviderType: AiProviderTypes.OpenAiCompatible));
                }
            }
        }

        var answerCalls = CountStarted(events, "KnowledgeAnswerAgent", "agent.started");
        if (answerCalls > 0)
        {
            var finalOutput = Math.Max(1, EstimateTokens(answer));
            result.Add(ForEndpoint("answer", CompatEndpoint(profile.AnswerEndpoint, profile.AnswerModel), answerCalls,
                answerCalls * (queryTokens + evidenceTokens + 256), answerCalls * finalOutput));
        }

        var reviewerCalls = CountStarted(events, "EvidenceReviewerAgent", "agent.started");
        if (reviewerCalls > 0)
            result.Add(ForEndpoint("reviewer", CompatEndpoint(profile.ReviewerEndpoint, profile.ReviewerModel), reviewerCalls,
                reviewerCalls * (queryTokens + evidenceTokens + Math.Max(1, EstimateTokens(answer))), reviewerCalls * 320));

        var specialistCalls = events.Count(x => string.Equals(x.Type, "tool.started", StringComparison.OrdinalIgnoreCase));
        if (specialistCalls > 0)
            result.Add(ForEndpoint("specialist", CompatEndpoint(profile.ReviewerEndpoint, profile.ReviewerModel), specialistCalls,
                specialistCalls * (queryTokens + evidenceTokens + 256), specialistCalls * 640));

        if (result.All(x => x.Operation != "answer") && !string.IsNullOrWhiteSpace(answer))
            result.Add(ForEndpoint("answer", CompatEndpoint(profile.AnswerEndpoint, profile.AnswerModel), 1,
                queryTokens + evidenceTokens + 256, Math.Max(1, EstimateTokens(answer))));

        return result;
    }

    public static int EstimateTokens(string? value)
        => string.IsNullOrWhiteSpace(value) ? 0 : Math.Max(1, (int)Math.Ceiling(value.Length / 4d));

    private static AiUsageEstimate ForEndpoint(
        string operation,
        ResolvedAiEndpoint endpoint,
        int calls,
        int inputTokens,
        int outputTokens)
        => new(operation, endpoint.Model, calls, inputTokens, outputTokens, true, endpoint.DeploymentId, endpoint.ProviderType);

    private static ResolvedAiEndpoint CompatEndpoint(ResolvedAiEndpoint endpoint, string model)
        => endpoint.DeploymentId is null && !string.IsNullOrWhiteSpace(model) &&
           !string.Equals(endpoint.Model, model, StringComparison.Ordinal)
            ? endpoint with { Model = model }
            : endpoint;

    private static int CountStarted(IEnumerable<RagProgressEvent> events, string actor, string type)
        => events.Count(x => string.Equals(x.Actor, actor, StringComparison.Ordinal) &&
                             string.Equals(x.Type, type, StringComparison.OrdinalIgnoreCase));

    private static string? ReadString(object? data, string property)
        => data?.GetType().GetProperty(property)?.GetValue(data)?.ToString();

    private static int ReadInt(object? data, string property)
    {
        var value = data?.GetType().GetProperty(property)?.GetValue(data);
        return value switch
        {
            int number => number,
            long number when number <= int.MaxValue => (int)number,
            _ => 0
        };
    }
}
