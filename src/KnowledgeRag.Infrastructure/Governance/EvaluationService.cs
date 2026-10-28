using System.Diagnostics;
using System.Text.Json;
using KnowledgeRag.Application.Security;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Conversations;
using KnowledgeRag.Infrastructure.KnowledgeBases;
using KnowledgeRag.Infrastructure.Persistence;
using KnowledgeRag.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeRag.Infrastructure.Governance;

public sealed class EvaluationService(
    AppDbContext db,
    RagAgentService rag,
    RagRuntimeProfileService runtimeProfiles,
    KnowledgeBaseAccessService knowledgeBaseAccess,
    TenantAccessService tenantAccess,
    WebhookAutomationService webhooks)
{
    private const string EvaluatorVersion = "citation-aware-v2";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public async Task<EvaluationRunEntity> RunAsync(ActorContext actor, Guid tenantId, Guid datasetId, CancellationToken ct = default)
    {
        await tenantAccess.RequireAsync(actor, tenantId, TenantRole.Admin, ct);
        var dataset = await db.Set<EvaluationDatasetEntity>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == datasetId && x.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Evaluation Dataset 不存在。");
        await knowledgeBaseAccess.RequireAsync(actor, dataset.KnowledgeBaseId, KnowledgeBaseRole.Viewer, ct);
        var caseRows = await db.Set<EvaluationCaseEntity>().AsNoTracking().Where(x => x.DatasetId == datasetId).ToListAsync(ct);
        var cases = caseRows.OrderBy(x => x.CreatedAt).ToList();
        if (cases.Count == 0) throw new InvalidOperationException("Evaluation Dataset 至少需要一个测试用例。");

        // Resolve once and persist only behavior/config identifiers. Managed prompt bodies are
        // intentionally excluded from Evaluation results so the report cannot become a prompt leak.
        var profile = await runtimeProfiles.ResolveAsync(dataset.KnowledgeBaseId, ct);
        var profileSnapshot = new EvaluationProfileSnapshot(
            profile.EnablePlanner,
            profile.PlannerMaxQueries,
            profile.MultiQueryTopK,
            profile.MaxMergedEvidence,
            profile.EnableReviewer,
            profile.ReviewerMaxRetries,
            profile.ReviewerMinConfidence,
            profile.PlannerModel,
            profile.AnswerModel,
            profile.ReviewerModel,
            profile.AgentMode,
            profile.PromptProfileId);

        // SQLite cannot ORDER BY DateTimeOffset server-side. Materialize the small per-dataset
        // history first, then select the newest completed baseline in memory for provider parity.
        var priorRuns = await db.Set<EvaluationRunEntity>().AsNoTracking()
            .Where(x => x.DatasetId == datasetId && x.Status == "completed")
            .ToListAsync(ct);
        var baseline = priorRuns.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
        var baselineSnapshot = baseline is null ? null : new EvaluationBaselineSnapshot(
            baseline.Id,
            baseline.PassRate,
            baseline.GroundednessRate,
            baseline.AverageLatencyMs,
            baseline.AverageSourceCount,
            baseline.CreatedAt);

        var run = new EvaluationRunEntity
        {
            Id = Guid.NewGuid(), DatasetId = datasetId, KnowledgeBaseId = dataset.KnowledgeBaseId,
            Status = "running", CaseCount = cases.Count, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
        };
        db.Add(run);
        await db.SaveChangesAsync(ct);

        var evaluated = new List<EvaluatedCase>(cases.Count);
        try
        {
            foreach (var item in cases)
            {
                ct.ThrowIfCancellationRequested();
                var timer = Stopwatch.StartNew();
                var response = await rag.AskAsync(dataset.KnowledgeBaseId, item.Query, null, ct);
                timer.Stop();

                var assessment = EvaluationScoring.Evaluate(item, response.Answer, response.Sources);
                evaluated.Add(new EvaluatedCase(assessment, timer.ElapsedMilliseconds));
            }

            var summary = Summarize(cases.Count, evaluated);
            var comparison = CompareWithBaseline(baselineSnapshot, summary);

            run.Status = "completed";
            run.PassedCount = summary.PassedCount;
            run.PassRate = summary.PassRate;
            run.GroundednessRate = summary.GroundednessRate;
            run.AverageLatencyMs = summary.AverageLatencyMs;
            // Preserve the existing column's meaning for API compatibility: retrieved evidence,
            // while the v2 JSON separately exposes actually cited-source averages.
            run.AverageSourceCount = summary.AverageRetrievedSourceCount;
            run.ResultsJson = SerializeDetails("completed", profileSnapshot, baselineSnapshot, comparison, summary, evaluated);
            run.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await webhooks.DispatchAsync(tenantId, "evaluation.completed", new
            {
                run.Id,
                run.DatasetId,
                run.PassRate,
                run.GroundednessRate,
                summary.CitationValidityRate,
                summary.NoAnswerAccuracy,
                run.AverageLatencyMs,
                baselineRunId = baselineSnapshot?.RunId,
                passRateRegression = comparison?.PassRateRegression,
                groundednessRegression = comparison?.GroundednessRegression,
                latencyRegression = comparison?.LatencyRegression
            }, ct);
            return run;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            var summary = Summarize(cases.Count, evaluated);
            run.Status = "cancelled";
            run.PassedCount = summary.PassedCount;
            run.PassRate = summary.PassRate;
            run.GroundednessRate = summary.GroundednessRate;
            run.AverageLatencyMs = summary.AverageLatencyMs;
            run.AverageSourceCount = summary.AverageRetrievedSourceCount;
            run.ResultsJson = SerializeDetails("cancelled", profileSnapshot, baselineSnapshot, null, summary, evaluated);
            run.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch
        {
            var summary = Summarize(cases.Count, evaluated);
            run.Status = "failed";
            run.PassedCount = summary.PassedCount;
            run.PassRate = summary.PassRate;
            run.GroundednessRate = summary.GroundednessRate;
            run.AverageLatencyMs = summary.AverageLatencyMs;
            run.AverageSourceCount = summary.AverageRetrievedSourceCount;
            run.ResultsJson = SerializeDetails("failed", profileSnapshot, baselineSnapshot, null, summary, evaluated);
            run.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private static EvaluationSummary Summarize(int totalCaseCount, IReadOnlyList<EvaluatedCase> evaluated)
    {
        var passedCount = evaluated.Count(x => x.Assessment.Passed);
        var passRate = totalCaseCount == 0 ? 0 : passedCount / (double)totalCaseCount;

        var groundingApplicable = evaluated.Where(x => x.Assessment.RequiresKnowledge || x.Assessment.IsNoAnswerCase).ToList();
        var groundednessRate = groundingApplicable.Count == 0
            ? 1
            : groundingApplicable.Count(x => x.Assessment.GroundingPass) / (double)groundingApplicable.Count;

        var citationApplicable = evaluated.Where(x => x.Assessment.RequiresKnowledge && !x.Assessment.IsNoAnswerCase).ToList();
        var citationValidityRate = citationApplicable.Count == 0
            ? 1
            : citationApplicable.Count(x => x.Assessment.CitationPass) / (double)citationApplicable.Count;

        var noAnswerCases = evaluated.Where(x => x.Assessment.IsNoAnswerCase).ToList();
        var noAnswerAccuracy = noAnswerCases.Count == 0
            ? 1
            : noAnswerCases.Count(x => x.Assessment.NoAnswerPass) / (double)noAnswerCases.Count;

        return new EvaluationSummary(
            totalCaseCount,
            evaluated.Count,
            passedCount,
            passRate,
            groundednessRate,
            citationValidityRate,
            noAnswerAccuracy,
            evaluated.Count == 0 ? 0 : evaluated.Average(x => (double)x.LatencyMs),
            evaluated.Count == 0 ? 0 : evaluated.Average(x => (double)x.Assessment.RetrievedSourceCount),
            evaluated.Count == 0 ? 0 : evaluated.Average(x => (double)x.Assessment.CitedSources.Count),
            BuildCategorySummaries(evaluated));
    }

    private static IReadOnlyList<EvaluationCategorySummary> BuildCategorySummaries(IReadOnlyList<EvaluatedCase> evaluated)
        => evaluated
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Assessment.Category) ? "uncategorized" : x.Assessment.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new EvaluationCategorySummary(
                group.Key,
                group.Count(),
                group.Count(x => x.Assessment.Passed),
                group.Count() == 0 ? 0 : group.Count(x => x.Assessment.Passed) / (double)group.Count(),
                group.Count(x => x.Assessment.GroundingPass),
                group.Count() == 0 ? 0 : group.Count(x => x.Assessment.GroundingPass) / (double)group.Count()))
            .ToList();

    private static EvaluationBaselineComparison? CompareWithBaseline(EvaluationBaselineSnapshot? baseline, EvaluationSummary summary)
    {
        if (baseline is null) return null;
        var latencyRegression = baseline.AverageLatencyMs > 0 && summary.AverageLatencyMs > baseline.AverageLatencyMs * 1.20;
        return new EvaluationBaselineComparison(
            baseline.RunId,
            summary.PassRate - baseline.PassRate,
            summary.GroundednessRate - baseline.GroundednessRate,
            summary.AverageLatencyMs - baseline.AverageLatencyMs,
            summary.PassRate + 1e-9 < baseline.PassRate,
            summary.GroundednessRate + 1e-9 < baseline.GroundednessRate,
            latencyRegression);
    }

    private static string SerializeDetails(
        string status,
        EvaluationProfileSnapshot profile,
        EvaluationBaselineSnapshot? baseline,
        EvaluationBaselineComparison? comparison,
        EvaluationSummary summary,
        IReadOnlyList<EvaluatedCase> evaluated)
    {
        var details = new EvaluationRunDetailsV2(
            2,
            EvaluatorVersion,
            status,
            profile,
            baseline,
            comparison,
            summary,
            evaluated.Select(x => new EvaluationCaseResultV2(
                x.Assessment.CaseId,
                x.Assessment.Category,
                x.Assessment.Query,
                x.Assessment.ExpectedAnswerContains,
                x.Assessment.RequiresKnowledge,
                x.Assessment.Answer,
                x.Assessment.RetrievedSourceCount,
                x.Assessment.CitedSources.Count,
                x.Assessment.CitationCount,
                x.Assessment.InvalidCitationCount,
                x.LatencyMs,
                new EvaluationChecksV2(
                    x.Assessment.NonEmpty,
                    x.Assessment.ExpectedPass,
                    x.Assessment.CitationPass,
                    x.Assessment.GroundingPass,
                    x.Assessment.NoAnswerPass,
                    x.Assessment.InsufficientEvidence),
                x.Assessment.Passed,
                x.Assessment.CitedSources.Select(source => new EvaluationSourceRef(
                    source.DocumentId,
                    source.FileName,
                    source.ChunkIndex,
                    source.PageNumber)).ToList()))
            .ToList());
        return JsonSerializer.Serialize(details, WebJson);
    }

    private sealed record EvaluatedCase(EvaluationCaseAssessment Assessment, long LatencyMs);

    private sealed record EvaluationProfileSnapshot(
        bool EnablePlanner,
        int PlannerMaxQueries,
        int MultiQueryTopK,
        int MaxMergedEvidence,
        bool EnableReviewer,
        int ReviewerMaxRetries,
        double ReviewerMinConfidence,
        string PlannerModel,
        string AnswerModel,
        string ReviewerModel,
        string AgentMode,
        Guid? PromptProfileId);

    private sealed record EvaluationBaselineSnapshot(
        Guid RunId,
        double PassRate,
        double GroundednessRate,
        double AverageLatencyMs,
        double AverageSourceCount,
        DateTimeOffset CreatedAt);

    private sealed record EvaluationBaselineComparison(
        Guid BaselineRunId,
        double PassRateDelta,
        double GroundednessDelta,
        double LatencyDeltaMs,
        bool PassRateRegression,
        bool GroundednessRegression,
        bool LatencyRegression);

    private sealed record EvaluationSummary(
        int CaseCount,
        int EvaluatedCaseCount,
        int PassedCount,
        double PassRate,
        double GroundednessRate,
        double CitationValidityRate,
        double NoAnswerAccuracy,
        double AverageLatencyMs,
        double AverageRetrievedSourceCount,
        double AverageCitedSourceCount,
        IReadOnlyList<EvaluationCategorySummary> Categories);

    private sealed record EvaluationCategorySummary(
        string Category,
        int CaseCount,
        int PassedCount,
        double PassRate,
        int GroundedCount,
        double GroundednessRate);

    private sealed record EvaluationRunDetailsV2(
        int SchemaVersion,
        string EvaluatorVersion,
        string Status,
        EvaluationProfileSnapshot Profile,
        EvaluationBaselineSnapshot? Baseline,
        EvaluationBaselineComparison? Comparison,
        EvaluationSummary Summary,
        IReadOnlyList<EvaluationCaseResultV2> Cases);

    private sealed record EvaluationCaseResultV2(
        Guid CaseId,
        string Category,
        string Query,
        string? ExpectedAnswerContains,
        bool RequiresKnowledge,
        string Answer,
        int RetrievedSourceCount,
        int CitedSourceCount,
        int CitationCount,
        int InvalidCitationCount,
        long LatencyMs,
        EvaluationChecksV2 Checks,
        bool Passed,
        IReadOnlyList<EvaluationSourceRef> CitedSources);

    private sealed record EvaluationChecksV2(
        bool NonEmpty,
        bool ExpectedPass,
        bool CitationPass,
        bool GroundingPass,
        bool NoAnswerPass,
        bool InsufficientEvidence);

    private sealed record EvaluationSourceRef(Guid DocumentId, string FileName, int ChunkIndex, int? PageNumber);
}
