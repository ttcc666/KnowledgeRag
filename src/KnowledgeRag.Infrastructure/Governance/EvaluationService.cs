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
    KnowledgeBaseAccessService knowledgeBaseAccess,
    TenantAccessService tenantAccess,
    WebhookAutomationService webhooks)
{
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

        var run = new EvaluationRunEntity
        {
            Id = Guid.NewGuid(), DatasetId = datasetId, KnowledgeBaseId = dataset.KnowledgeBaseId,
            Status = "running", CaseCount = cases.Count, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
        };
        db.Add(run);
        await db.SaveChangesAsync(ct);

        var results = new List<object>(cases.Count);
        var latencies = new List<double>(cases.Count);
        var sourceCounts = new List<int>(cases.Count);
        var passed = 0;
        var grounded = 0;
        try
        {
            foreach (var item in cases)
            {
                ct.ThrowIfCancellationRequested();
                var timer = Stopwatch.StartNew();
                var response = await rag.AskAsync(dataset.KnowledgeBaseId, item.Query, null, ct);
                timer.Stop();
                var answer = response.Answer ?? string.Empty;
                var hasSources = response.Sources.Count > 0;
                var insufficient = answer.Contains("证据不足", StringComparison.OrdinalIgnoreCase)
                                   || answer.Contains("没有找到足够信息", StringComparison.OrdinalIgnoreCase)
                                   || answer.Contains("没有足够信息", StringComparison.OrdinalIgnoreCase);
                var expectedPass = string.IsNullOrWhiteSpace(item.ExpectedAnswerContains)
                                   || answer.Contains(item.ExpectedAnswerContains, StringComparison.OrdinalIgnoreCase);
                var groundingPass = !item.RequiresKnowledge || hasSources || insufficient;
                var noAnswerPass = !string.Equals(item.Category, "no-answer", StringComparison.OrdinalIgnoreCase) || insufficient;
                var ok = !string.IsNullOrWhiteSpace(answer) && expectedPass && groundingPass && noAnswerPass;
                if (ok) passed++;
                if (groundingPass) grounded++;
                latencies.Add(timer.Elapsed.TotalMilliseconds);
                sourceCounts.Add(response.Sources.Count);
                results.Add(new
                {
                    item.Id, item.Category, item.Query, item.ExpectedAnswerContains,
                    answer, sourceCount = response.Sources.Count, latencyMs = timer.ElapsedMilliseconds,
                    checks = new { nonEmpty = !string.IsNullOrWhiteSpace(answer), expectedPass, groundingPass, noAnswerPass }, passed = ok
                });
            }

            run.Status = "completed";
            run.PassedCount = passed;
            run.PassRate = cases.Count == 0 ? 0 : passed / (double)cases.Count;
            run.GroundednessRate = cases.Count == 0 ? 0 : grounded / (double)cases.Count;
            run.AverageLatencyMs = latencies.Count == 0 ? 0 : latencies.Average();
            run.AverageSourceCount = sourceCounts.Count == 0 ? 0 : sourceCounts.Average();
            run.ResultsJson = JsonSerializer.Serialize(results, WebJson);
            run.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await webhooks.DispatchAsync(tenantId, "evaluation.completed", new { run.Id, run.DatasetId, run.PassRate, run.GroundednessRate, run.AverageLatencyMs }, ct);
            return run;
        }
        catch
        {
            run.Status = "failed";
            run.ResultsJson = JsonSerializer.Serialize(results, WebJson);
            run.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }
}
