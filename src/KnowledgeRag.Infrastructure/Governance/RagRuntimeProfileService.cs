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

/// <summary>
/// Resolves an immutable RAG execution snapshot once per scoped request. Conversation budget
/// reservation, Agent execution and Evaluation therefore observe the exact same model/prompt/policy
/// values even if an administrator updates the persisted profile concurrently.
/// </summary>
public sealed class RagRuntimeProfileService(
    AppDbContext db,
    IOptions<AiOptions> aiOptions,
    IOptions<RagOptions> ragOptions)
{
    private readonly AiOptions _ai = aiOptions.Value;
    private readonly RagOptions _rag = ragOptions.Value;
    private readonly Dictionary<Guid, ResolvedRagProfile> _resolved = new();

    public async Task<ResolvedRagProfile> ResolveAsync(Guid knowledgeBaseId, CancellationToken ct = default)
    {
        if (_resolved.TryGetValue(knowledgeBaseId, out var cached)) return cached;

        var profile = await db.Set<KnowledgeBaseRagProfileEntity>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.KnowledgeBaseId == knowledgeBaseId, ct);

        PromptProfileEntity? prompt = null;
        if (profile?.PromptProfileId is Guid promptId)
            prompt = await db.Set<PromptProfileEntity>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == promptId, ct);
        prompt ??= await db.Set<PromptProfileEntity>().AsNoTracking()
            .Where(x => x.KnowledgeBaseId == knowledgeBaseId && x.IsActive)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);

        var resolved = new ResolvedRagProfile(
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
            AppendMandatoryPolicy(prompt?.AnswerPrompt, AnswerCitationPolicy),
            AppendMandatoryPolicy(prompt?.ReviewerPrompt, ReviewerCitationPolicy));

        _resolved[knowledgeBaseId] = resolved;
        return resolved;
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

    private static string AppendMandatoryPolicy(string? managedPrompt, string mandatoryPolicy)
        => string.IsNullOrWhiteSpace(managedPrompt)
            ? mandatoryPolicy
            : managedPrompt.Trim() + "\n\n" + mandatoryPolicy;

    private const string AnswerCitationPolicy = """
        ## 强制引用契约
        Evidence 片段使用 [E1]、[E2]... 标识。所有来自知识库的关键事实结论必须在对应句子后使用一个或多个 [E#] 引用。
        只能引用当前提供且真实存在的 Evidence ID；不得伪造、猜测或引用不存在的编号。
        不要用文件名、页码或裸 URL 代替 [E#]。Evidence 不足时直接说明不足，不得为了满足格式而制造引用。
        """;

    private const string ReviewerCitationPolicy = """
        ## 强制引用审核契约
        将 [E#] 视为机器可验证的 Evidence 引用。检查每个重要知识库事实是否有对应引用，并确认编号存在且内容支持该 claim。
        缺失引用、引用不存在、引用与 claim 不匹配时写入 CitationErrors，并令 Supported=false。不得因为格式偏好之外的问题误判。
        """;
}
