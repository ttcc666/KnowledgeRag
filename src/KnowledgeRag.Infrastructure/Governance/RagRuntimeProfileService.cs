using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Configuration;
using KnowledgeRag.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.Governance;

public sealed record ResolvedRagProfile(
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
    decimal MonthlyBudgetUsd,
    int DailyRunLimit,
    Guid? PromptProfileId,
    string? PlannerPrompt,
    string? AnswerPrompt,
    string? ReviewerPrompt);

public sealed class RagRuntimeProfileService(
    AppDbContext db,
    IOptions<AiOptions> aiOptions,
    IOptions<RagOptions> ragOptions)
{
    private readonly AiOptions _ai = aiOptions.Value;
    private readonly RagOptions _rag = ragOptions.Value;

    public async Task<ResolvedRagProfile> ResolveAsync(Guid knowledgeBaseId, CancellationToken ct = default)
    {
        var profile = await db.Set<KnowledgeBaseRagProfileEntity>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.KnowledgeBaseId == knowledgeBaseId, ct);

        PromptProfileEntity? prompt = null;
        if (profile?.PromptProfileId is Guid promptId)
            prompt = await db.Set<PromptProfileEntity>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == promptId, ct);
        prompt ??= await db.Set<PromptProfileEntity>().AsNoTracking()
            .Where(x => x.KnowledgeBaseId == knowledgeBaseId && x.IsActive)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);

        return new ResolvedRagProfile(
            profile?.EnablePlanner ?? _rag.EnablePlanner,
            Math.Clamp(profile?.PlannerMaxQueries ?? _rag.PlannerMaxQueries, 1, 6),
            Math.Clamp(profile?.MultiQueryTopK ?? _rag.MultiQueryTopK, 1, 30),
            Math.Clamp(profile?.MaxMergedEvidence ?? _rag.MaxMergedEvidence, 1, 40),
            profile?.EnableReviewer ?? _rag.EnableReviewer,
            Math.Clamp(profile?.ReviewerMaxRetries ?? _rag.ReviewerMaxRetries, 0, 3),
            Math.Clamp(profile?.ReviewerMinConfidence ?? _rag.ReviewerMinConfidence, 0, 1),
            string.IsNullOrWhiteSpace(profile?.PlannerModel) ? _ai.ResolvePlannerModel() : profile!.PlannerModel,
            string.IsNullOrWhiteSpace(profile?.AnswerModel) ? _ai.ResolveAnswerModel() : profile!.AnswerModel,
            string.IsNullOrWhiteSpace(profile?.ReviewerModel) ? _ai.ResolveReviewerModel() : profile!.ReviewerModel,
            string.IsNullOrWhiteSpace(profile?.AgentMode) ? "specialists" : profile!.AgentMode,
            profile?.MonthlyBudgetUsd ?? 0,
            profile?.DailyRunLimit ?? 0,
            profile?.PromptProfileId,
            NullIfWhiteSpace(prompt?.PlannerPrompt),
            NullIfWhiteSpace(prompt?.AnswerPrompt),
            NullIfWhiteSpace(prompt?.ReviewerPrompt));
    }

    public async Task<decimal> EstimateCostAsync(Guid tenantId, string model, int inputTokens, int outputTokens, CancellationToken ct = default)
    {
        // SQLite cannot translate DateTimeOffset ORDER BY. Keep provider-neutral filtering in SQL,
        // then select the newest matching price in memory. PostgreSQL follows the same path so
        // cost selection semantics stay consistent across supported database providers.
        var prices = await db.Set<ModelPriceEntity>().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Model == model && x.IsActive)
            .ToListAsync(ct);
        var price = prices.OrderByDescending(x => x.UpdatedAt).FirstOrDefault();
        var inputRate = price?.InputCostPerMillionTokens ?? _ai.ChatInputCostPerMillionTokens;
        var outputRate = price?.OutputCostPerMillionTokens ?? _ai.ChatOutputCostPerMillionTokens;
        return inputTokens / 1_000_000m * inputRate + outputTokens / 1_000_000m * outputRate;
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
