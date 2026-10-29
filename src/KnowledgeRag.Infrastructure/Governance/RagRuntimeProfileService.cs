using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.AI;
using KnowledgeRag.Infrastructure.Configuration;
using KnowledgeRag.Infrastructure.Persistence;
using KnowledgeRag.Infrastructure.Tenancy;
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
    string? ReviewerPrompt)
{
    private static readonly ResolvedAiEndpoint PlatformDefault = new(
        null, null, AiProviderTypes.OpenAiCompatible, "https://api.openai.com/v1", string.Empty, "gpt-4o-mini", 0, false);

    // ChatEndpoint remains as a compatibility alias for code/tests that predate role-specific routing.
    public ResolvedAiEndpoint ChatEndpoint { get; init; } = PlatformDefault;
    public ResolvedAiEndpoint PlannerEndpoint { get; init; } = PlatformDefault;
    public ResolvedAiEndpoint AnswerEndpoint { get; init; } = PlatformDefault;
    public ResolvedAiEndpoint ReviewerEndpoint { get; init; } = PlatformDefault;
    public ResolvedAiEndpoint? RerankerEndpoint { get; init; }
    public EmbeddingEndpointResolution? EmbeddingEndpoint { get; init; }
}

/// <summary>
/// Resolves one immutable RAG execution snapshot per scoped request. Explicit deployment bindings
/// are resolved (including deterministic persisted-health fallback) before budget reservation and
/// Agent execution. No provider/network health decision is made mid-run.
/// </summary>
public sealed class RagRuntimeProfileService(
    AppDbContext db,
    TenantAiProviderService tenantAiProviders,
    IOptions<AiOptions> aiOptions,
    IOptions<RagOptions> ragOptions)
{
    private readonly AiOptions _ai = aiOptions.Value;
    private readonly RagOptions _rag = ragOptions.Value;
    private readonly Dictionary<Guid, ResolvedRagProfile> _resolved = new();

    public async Task<ResolvedRagProfile> ResolveAsync(Guid knowledgeBaseId, CancellationToken ct = default)
    {
        if (_resolved.TryGetValue(knowledgeBaseId, out var cached)) return cached;

        var knowledgeBase = await db.KnowledgeBases.AsNoTracking()
            .Where(x => x.Id == knowledgeBaseId)
            .Select(x => new { x.TenantId, x.ActiveIndexGenerationId })
            .SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("知识库不存在。");
        var tenantId = knowledgeBase.TenantId;

        var profile = await db.Set<KnowledgeBaseRagProfileEntity>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.KnowledgeBaseId == knowledgeBaseId, ct);

        PromptProfileEntity? prompt = null;
        if (profile?.PromptProfileId is Guid promptId)
            prompt = await db.Set<PromptProfileEntity>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == promptId, ct);
        prompt ??= await db.Set<PromptProfileEntity>().AsNoTracking()
            .Where(x => x.KnowledgeBaseId == knowledgeBaseId && x.IsActive)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);

        var tenantChat = await tenantAiProviders.ResolveDefaultEndpointAsync(tenantId, AiModelTypes.Chat, ct);
        var plannerEndpoint = await ResolveChatRoleAsync(
            tenantId, profile?.PlannerDeploymentId, profile?.PlannerModel, tenantChat, _ai.ResolvePlannerModel(), ct);
        var answerEndpoint = await ResolveChatRoleAsync(
            tenantId, profile?.AnswerDeploymentId, profile?.AnswerModel, tenantChat, _ai.ResolveAnswerModel(), ct);
        var reviewerEndpoint = await ResolveChatRoleAsync(
            tenantId, profile?.ReviewerDeploymentId, profile?.ReviewerModel, tenantChat, _ai.ResolveReviewerModel(), ct);
        var rerankerEndpoint = profile?.RerankerDeploymentId is Guid rerankerDeploymentId
            ? await tenantAiProviders.ResolveEndpointWithFallbackAsync(tenantId, rerankerDeploymentId, AiModelTypes.Rerank, ct)
            : await tenantAiProviders.ResolveDefaultEndpointAsync(tenantId, AiModelTypes.Rerank, ct);
        var embeddingEndpoint = await tenantAiProviders.ResolveEmbeddingEndpointForGenerationAsync(
            knowledgeBaseId, knowledgeBase.ActiveIndexGenerationId, ct);

        var resolved = new ResolvedRagProfile(
            profile?.EnablePlanner ?? _rag.EnablePlanner,
            Math.Clamp(profile?.PlannerMaxQueries ?? _rag.PlannerMaxQueries, 1, 6),
            Math.Clamp(profile?.MultiQueryTopK ?? _rag.MultiQueryTopK, 1, 30),
            Math.Clamp(profile?.MaxMergedEvidence ?? _rag.MaxMergedEvidence, 1, 40),
            profile?.EnableReviewer ?? _rag.EnableReviewer,
            Math.Clamp(profile?.ReviewerMaxRetries ?? _rag.ReviewerMaxRetries, 0, 3),
            Math.Clamp(profile?.ReviewerMinConfidence ?? _rag.ReviewerMinConfidence, 0, 1),
            plannerEndpoint.Model,
            answerEndpoint.Model,
            reviewerEndpoint.Model,
            string.IsNullOrWhiteSpace(profile?.AgentMode) ? "specialists" : profile!.AgentMode,
            profile?.MonthlyBudgetUsd ?? 0,
            profile?.DailyRunLimit ?? 0,
            profile?.PromptProfileId,
            NullIfWhiteSpace(prompt?.PlannerPrompt),
            AppendMandatoryPolicy(prompt?.AnswerPrompt, AnswerCitationPolicy),
            AppendMandatoryPolicy(prompt?.ReviewerPrompt, ReviewerCitationPolicy))
        {
            ChatEndpoint = answerEndpoint,
            PlannerEndpoint = plannerEndpoint,
            AnswerEndpoint = answerEndpoint,
            ReviewerEndpoint = reviewerEndpoint,
            RerankerEndpoint = rerankerEndpoint,
            EmbeddingEndpoint = embeddingEndpoint
        };

        _resolved[knowledgeBaseId] = resolved;
        return resolved;
    }

    public Task<decimal> EstimateCostAsync(
        Guid tenantId,
        string model,
        int inputTokens,
        int outputTokens,
        CancellationToken ct = default)
        => EstimateCostAsync(tenantId, model, null, inputTokens, outputTokens, ct);

    public async Task<decimal> EstimateCostAsync(
        Guid tenantId,
        string model,
        Guid? deploymentId,
        int inputTokens,
        int outputTokens,
        CancellationToken ct = default)
    {
        var prices = await db.Set<ModelPriceEntity>().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive &&
                        ((deploymentId.HasValue && x.DeploymentId == deploymentId) ||
                         (!x.DeploymentId.HasValue && x.Model == model)))
            .ToListAsync(ct);
        var price = deploymentId.HasValue
            ? prices.Where(x => x.DeploymentId == deploymentId).OrderByDescending(x => x.UpdatedAt).FirstOrDefault()
              ?? prices.Where(x => x.DeploymentId == null && x.Model == model).OrderByDescending(x => x.UpdatedAt).FirstOrDefault()
            : prices.Where(x => x.DeploymentId == null && x.Model == model).OrderByDescending(x => x.UpdatedAt).FirstOrDefault();
        var inputRate = price?.InputCostPerMillionTokens ?? _ai.ChatInputCostPerMillionTokens;
        var outputRate = price?.OutputCostPerMillionTokens ?? _ai.ChatOutputCostPerMillionTokens;
        return inputTokens / 1_000_000m * inputRate + outputTokens / 1_000_000m * outputRate;
    }

    private async Task<ResolvedAiEndpoint> ResolveChatRoleAsync(
        Guid tenantId,
        Guid? deploymentId,
        string? legacyModel,
        ResolvedAiEndpoint? tenantDefault,
        string platformModel,
        CancellationToken ct)
    {
        if (deploymentId is Guid explicitDeploymentId)
            return await tenantAiProviders.ResolveEndpointWithFallbackAsync(tenantId, explicitDeploymentId, AiModelTypes.Chat, ct);

        if (!string.IsNullOrWhiteSpace(legacyModel))
        {
            var baseEndpoint = tenantDefault ?? PlatformChatEndpoint(platformModel);
            // A raw model override is not a deployment identity. Preserve the historical endpoint
            // behavior but do not charge it against the tenant default deployment's price.
            return baseEndpoint with { DeploymentId = null, Model = legacyModel.Trim() };
        }

        return tenantDefault ?? PlatformChatEndpoint(platformModel);
    }

    private ResolvedAiEndpoint PlatformChatEndpoint(string model)
        => new(null, null, AiProviderTypes.OpenAiCompatible, _ai.BaseUrl, _ai.ApiKey, model, 0, false);

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
