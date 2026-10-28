using System.Text.Json;
using KnowledgeRag.Application.Security;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.KnowledgeBases;
using KnowledgeRag.Infrastructure.Persistence;
using KnowledgeRag.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeRag.Infrastructure.Governance;

public sealed record GovernanceOverview(
    int RunCount,
    double AverageLatencyMs,
    decimal EstimatedCostUsd,
    int AuditCount,
    int PromptProfileCount,
    int ActiveWebhookCount,
    int EvaluationRunCount);

public sealed record RagProfileUpdate(
    bool EnablePlanner,
    int PlannerMaxQueries,
    int MultiQueryTopK,
    int MaxMergedEvidence,
    bool EnableReviewer,
    int ReviewerMaxRetries,
    double ReviewerMinConfidence,
    string? PlannerModel,
    string? AnswerModel,
    string? ReviewerModel,
    Guid? PromptProfileId,
    string? AgentMode,
    decimal MonthlyBudgetUsd,
    int DailyRunLimit);

public sealed record ModelPriceUpdate(string Model, decimal InputCostPerMillionTokens, decimal OutputCostPerMillionTokens, bool IsActive = true);
public sealed record PromptProfileUpsert(Guid? Id, Guid? KnowledgeBaseId, string Name, string PlannerPrompt, string AnswerPrompt, string ReviewerPrompt, bool IsActive);
public sealed record WebhookUpsert(Guid? Id, string Name, string Url, IReadOnlyList<string> EventTypes, string? Secret, bool IsActive);
public sealed record EvaluationDatasetCreate(Guid KnowledgeBaseId, string Name, string? Description);
public sealed record EvaluationCaseCreate(string Category, string Query, string? ExpectedAnswerContains, bool RequiresKnowledge = true);

public sealed class GovernanceService(
    AppDbContext db,
    KnowledgeBaseAccessService knowledgeBaseAccess,
    TenantAccessService tenantAccess,
    RagRuntimeProfileService runtimeProfiles)
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public async Task<GovernanceOverview> GetOverviewAsync(ActorContext actor, Guid tenantId, CancellationToken ct = default)
    {
        await tenantAccess.RequireAsync(actor, tenantId, TenantRole.Member, ct);
        var runs = db.Set<RagRunEntity>().AsNoTracking().Where(x => x.TenantId == tenantId);
        var runCount = await runs.CountAsync(ct);
        var avgLatency = runCount == 0 ? 0 : await runs.AverageAsync(x => (double)x.LatencyMs, ct);
        var cost = await runs.SumAsync(x => x.EstimatedCostUsd, ct);
        return new GovernanceOverview(
            runCount,
            avgLatency,
            cost,
            await db.Set<AuditLogEntity>().AsNoTracking().CountAsync(x => x.TenantId == tenantId, ct),
            await db.Set<PromptProfileEntity>().AsNoTracking().CountAsync(x => x.TenantId == tenantId, ct),
            await db.Set<WebhookSubscriptionEntity>().AsNoTracking().CountAsync(x => x.TenantId == tenantId && x.IsActive, ct),
            await db.Set<EvaluationRunEntity>().AsNoTracking().CountAsync(x => db.Set<EvaluationDatasetEntity>().Any(d => d.Id == x.DatasetId && d.TenantId == tenantId), ct));
    }

    public async Task<KnowledgeBaseRagProfileEntity> GetRagProfileAsync(ActorContext actor, Guid knowledgeBaseId, CancellationToken ct = default)
    {
        await knowledgeBaseAccess.RequireAsync(actor, knowledgeBaseId, KnowledgeBaseRole.Viewer, ct);
        var existing = await db.Set<KnowledgeBaseRagProfileEntity>().AsNoTracking().SingleOrDefaultAsync(x => x.KnowledgeBaseId == knowledgeBaseId, ct);
        if (existing is not null) return existing;
        var resolved = await runtimeProfiles.ResolveAsync(knowledgeBaseId, ct);
        return new KnowledgeBaseRagProfileEntity
        {
            KnowledgeBaseId = knowledgeBaseId,
            EnablePlanner = resolved.EnablePlanner,
            PlannerMaxQueries = resolved.PlannerMaxQueries,
            MultiQueryTopK = resolved.MultiQueryTopK,
            MaxMergedEvidence = resolved.MaxMergedEvidence,
            EnableReviewer = resolved.EnableReviewer,
            ReviewerMaxRetries = resolved.ReviewerMaxRetries,
            ReviewerMinConfidence = resolved.ReviewerMinConfidence,
            PlannerModel = resolved.PlannerModel,
            AnswerModel = resolved.AnswerModel,
            ReviewerModel = resolved.ReviewerModel,
            PromptProfileId = resolved.PromptProfileId,
            AgentMode = resolved.AgentMode,
            MonthlyBudgetUsd = resolved.MonthlyBudgetUsd,
            DailyRunLimit = resolved.DailyRunLimit,
            UpdatedByUserId = actor.UserId,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<KnowledgeBaseRagProfileEntity> SaveRagProfileAsync(ActorContext actor, Guid knowledgeBaseId, RagProfileUpdate request, CancellationToken ct = default)
    {
        await knowledgeBaseAccess.RequireAsync(actor, knowledgeBaseId, KnowledgeBaseRole.Owner, ct);
        var entity = await db.Set<KnowledgeBaseRagProfileEntity>().SingleOrDefaultAsync(x => x.KnowledgeBaseId == knowledgeBaseId, ct)
                     ?? new KnowledgeBaseRagProfileEntity { KnowledgeBaseId = knowledgeBaseId };
        if (db.Entry(entity).State == EntityState.Detached) db.Add(entity);
        entity.EnablePlanner = request.EnablePlanner;
        entity.PlannerMaxQueries = Math.Clamp(request.PlannerMaxQueries, 1, 6);
        entity.MultiQueryTopK = Math.Clamp(request.MultiQueryTopK, 1, 30);
        entity.MaxMergedEvidence = Math.Clamp(request.MaxMergedEvidence, 1, 40);
        entity.EnableReviewer = request.EnableReviewer;
        entity.ReviewerMaxRetries = Math.Clamp(request.ReviewerMaxRetries, 0, 3);
        entity.ReviewerMinConfidence = Math.Clamp(request.ReviewerMinConfidence, 0, 1);
        entity.PlannerModel = request.PlannerModel?.Trim() ?? string.Empty;
        entity.AnswerModel = request.AnswerModel?.Trim() ?? string.Empty;
        entity.ReviewerModel = request.ReviewerModel?.Trim() ?? string.Empty;
        entity.PromptProfileId = request.PromptProfileId;
        entity.AgentMode = string.IsNullOrWhiteSpace(request.AgentMode) ? "specialists" : request.AgentMode.Trim();
        entity.MonthlyBudgetUsd = Math.Max(0, request.MonthlyBudgetUsd);
        entity.DailyRunLimit = Math.Max(0, request.DailyRunLimit);
        entity.UpdatedByUserId = actor.UserId;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<IReadOnlyList<RagRunEntity>> ListRunsAsync(ActorContext actor, Guid knowledgeBaseId, int limit = 100, CancellationToken ct = default)
    {
        await knowledgeBaseAccess.RequireAsync(actor, knowledgeBaseId, KnowledgeBaseRole.Viewer, ct);
        return await db.Set<RagRunEntity>().AsNoTracking().Where(x => x.KnowledgeBaseId == knowledgeBaseId)
            .OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(limit, 1, 500)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AuditLogEntity>> ListAuditAsync(ActorContext actor, Guid tenantId, int limit = 200, CancellationToken ct = default)
    {
        await tenantAccess.RequireAsync(actor, tenantId, TenantRole.Admin, ct);
        return await db.Set<AuditLogEntity>().AsNoTracking().Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(limit, 1, 1000)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ModelPriceEntity>> ListModelPricesAsync(ActorContext actor, Guid tenantId, CancellationToken ct = default)
    {
        await tenantAccess.RequireAsync(actor, tenantId, TenantRole.Member, ct);
        return await db.Set<ModelPriceEntity>().AsNoTracking().Where(x => x.TenantId == tenantId).OrderBy(x => x.Model).ToListAsync(ct);
    }

    public async Task<ModelPriceEntity> SaveModelPriceAsync(ActorContext actor, Guid tenantId, ModelPriceUpdate request, CancellationToken ct = default)
    {
        await tenantAccess.RequireAsync(actor, tenantId, TenantRole.Admin, ct);
        if (string.IsNullOrWhiteSpace(request.Model)) throw new InvalidOperationException("模型名称不能为空。");
        var model = request.Model.Trim();
        var entity = await db.Set<ModelPriceEntity>().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Model == model, ct)
                     ?? new ModelPriceEntity { Id = Guid.NewGuid(), TenantId = tenantId, Model = model };
        if (db.Entry(entity).State == EntityState.Detached) db.Add(entity);
        entity.InputCostPerMillionTokens = Math.Max(0, request.InputCostPerMillionTokens);
        entity.OutputCostPerMillionTokens = Math.Max(0, request.OutputCostPerMillionTokens);
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<IReadOnlyList<PromptProfileEntity>> ListPromptProfilesAsync(ActorContext actor, Guid tenantId, CancellationToken ct = default)
    {
        await tenantAccess.RequireAsync(actor, tenantId, TenantRole.Member, ct);
        return await db.Set<PromptProfileEntity>().AsNoTracking().Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.UpdatedAt).ToListAsync(ct);
    }

    public async Task<PromptProfileEntity> SavePromptProfileAsync(ActorContext actor, Guid tenantId, PromptProfileUpsert request, CancellationToken ct = default)
    {
        await tenantAccess.RequireAsync(actor, tenantId, TenantRole.Admin, ct);
        if (string.IsNullOrWhiteSpace(request.Name)) throw new InvalidOperationException("Prompt Profile 名称不能为空。");
        if (request.KnowledgeBaseId is Guid kbId)
            await knowledgeBaseAccess.RequireAsync(actor, kbId, KnowledgeBaseRole.Owner, ct);
        var now = DateTimeOffset.UtcNow;
        var entity = request.Id is Guid id
            ? await db.Set<PromptProfileEntity>().SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct)
            : null;
        if (entity is null)
        {
            var nextVersion = 1 + await db.Set<PromptProfileEntity>().Where(x => x.TenantId == tenantId && x.Name == request.Name).Select(x => (int?)x.Version).MaxAsync(ct) ?? 1;
            entity = new PromptProfileEntity { Id = Guid.NewGuid(), TenantId = tenantId, CreatedAt = now, Version = nextVersion };
            db.Add(entity);
        }
        entity.KnowledgeBaseId = request.KnowledgeBaseId;
        entity.Name = request.Name.Trim();
        entity.PlannerPrompt = request.PlannerPrompt ?? string.Empty;
        entity.AnswerPrompt = request.AnswerPrompt ?? string.Empty;
        entity.ReviewerPrompt = request.ReviewerPrompt ?? string.Empty;
        entity.IsActive = request.IsActive;
        entity.UpdatedByUserId = actor.UserId;
        entity.UpdatedAt = now;
        if (entity.IsActive && entity.KnowledgeBaseId is Guid activeKb)
        {
            var others = await db.Set<PromptProfileEntity>().Where(x => x.Id != entity.Id && x.KnowledgeBaseId == activeKb && x.IsActive).ToListAsync(ct);
            foreach (var other in others) other.IsActive = false;
        }
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<IReadOnlyList<WebhookSubscriptionEntity>> ListWebhooksAsync(ActorContext actor, Guid tenantId, CancellationToken ct = default)
    {
        await tenantAccess.RequireAsync(actor, tenantId, TenantRole.Admin, ct);
        return await db.Set<WebhookSubscriptionEntity>().AsNoTracking().Where(x => x.TenantId == tenantId).OrderByDescending(x => x.UpdatedAt).ToListAsync(ct);
    }

    public async Task<WebhookSubscriptionEntity> SaveWebhookAsync(ActorContext actor, Guid tenantId, WebhookUpsert request, CancellationToken ct = default)
    {
        await tenantAccess.RequireAsync(actor, tenantId, TenantRole.Admin, ct);
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Webhook URL 必须是有效的 http/https 地址。");
        var now = DateTimeOffset.UtcNow;
        var entity = request.Id is Guid id
            ? await db.Set<WebhookSubscriptionEntity>().SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct)
            : null;
        if (entity is null)
        {
            entity = new WebhookSubscriptionEntity { Id = Guid.NewGuid(), TenantId = tenantId, CreatedByUserId = actor.UserId, CreatedAt = now };
            db.Add(entity);
        }
        entity.Name = request.Name.Trim();
        entity.Url = request.Url.Trim();
        entity.EventTypes = string.Join(',', request.EventTypes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(request.Secret)) entity.Secret = request.Secret.Trim();
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task DeleteWebhookAsync(ActorContext actor, Guid tenantId, Guid webhookId, CancellationToken ct = default)
    {
        await tenantAccess.RequireAsync(actor, tenantId, TenantRole.Admin, ct);
        var entity = await db.Set<WebhookSubscriptionEntity>().SingleOrDefaultAsync(x => x.Id == webhookId && x.TenantId == tenantId, ct)
                     ?? throw new KeyNotFoundException("Webhook 不存在。");
        db.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task<EvaluationDatasetEntity> CreateEvaluationDatasetAsync(ActorContext actor, Guid tenantId, EvaluationDatasetCreate request, CancellationToken ct = default)
    {
        await tenantAccess.RequireAsync(actor, tenantId, TenantRole.Admin, ct);
        await knowledgeBaseAccess.RequireAsync(actor, request.KnowledgeBaseId, KnowledgeBaseRole.Viewer, ct);
        var now = DateTimeOffset.UtcNow;
        var entity = new EvaluationDatasetEntity { Id = Guid.NewGuid(), TenantId = tenantId, KnowledgeBaseId = request.KnowledgeBaseId, Name = request.Name.Trim(), Description = request.Description?.Trim(), CreatedByUserId = actor.UserId, CreatedAt = now, UpdatedAt = now };
        db.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<IReadOnlyList<EvaluationDatasetEntity>> ListEvaluationDatasetsAsync(ActorContext actor, Guid tenantId, CancellationToken ct = default)
    {
        await tenantAccess.RequireAsync(actor, tenantId, TenantRole.Member, ct);
        return await db.Set<EvaluationDatasetEntity>().AsNoTracking().Where(x => x.TenantId == tenantId).OrderByDescending(x => x.UpdatedAt).ToListAsync(ct);
    }

    public async Task<EvaluationCaseEntity> AddEvaluationCaseAsync(ActorContext actor, Guid tenantId, Guid datasetId, EvaluationCaseCreate request, CancellationToken ct = default)
    {
        await tenantAccess.RequireAsync(actor, tenantId, TenantRole.Admin, ct);
        var dataset = await db.Set<EvaluationDatasetEntity>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == datasetId && x.TenantId == tenantId, ct)
                      ?? throw new KeyNotFoundException("Evaluation Dataset 不存在。");
        await knowledgeBaseAccess.RequireAsync(actor, dataset.KnowledgeBaseId, KnowledgeBaseRole.Viewer, ct);
        var entity = new EvaluationCaseEntity { Id = Guid.NewGuid(), DatasetId = datasetId, Category = string.IsNullOrWhiteSpace(request.Category) ? "fact" : request.Category.Trim(), Query = request.Query.Trim(), ExpectedAnswerContains = request.ExpectedAnswerContains?.Trim(), RequiresKnowledge = request.RequiresKnowledge, CreatedAt = DateTimeOffset.UtcNow };
        db.Add(entity);
        dataset.UpdatedAt = DateTimeOffset.UtcNow;
        db.Update(dataset);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<IReadOnlyList<EvaluationCaseEntity>> ListEvaluationCasesAsync(ActorContext actor, Guid tenantId, Guid datasetId, CancellationToken ct = default)
    {
        await tenantAccess.RequireAsync(actor, tenantId, TenantRole.Member, ct);
        var exists = await db.Set<EvaluationDatasetEntity>().AsNoTracking().AnyAsync(x => x.Id == datasetId && x.TenantId == tenantId, ct);
        if (!exists) throw new KeyNotFoundException("Evaluation Dataset 不存在。");
        return await db.Set<EvaluationCaseEntity>().AsNoTracking().Where(x => x.DatasetId == datasetId).OrderBy(x => x.CreatedAt).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<EvaluationRunEntity>> ListEvaluationRunsAsync(ActorContext actor, Guid tenantId, Guid? datasetId = null, CancellationToken ct = default)
    {
        await tenantAccess.RequireAsync(actor, tenantId, TenantRole.Member, ct);
        var datasetIds = db.Set<EvaluationDatasetEntity>().AsNoTracking().Where(x => x.TenantId == tenantId).Select(x => x.Id);
        var query = db.Set<EvaluationRunEntity>().AsNoTracking().Where(x => datasetIds.Contains(x.DatasetId));
        if (datasetId.HasValue) query = query.Where(x => x.DatasetId == datasetId.Value);
        return await query.OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(ct);
    }

    public async Task<Guid> ResolveTenantIdAsync(Guid knowledgeBaseId, CancellationToken ct = default)
        => await db.KnowledgeBases.AsNoTracking().Where(x => x.Id == knowledgeBaseId).Select(x => x.TenantId).SingleAsync(ct);

    public async Task<RagRunEntity> RecordRunAsync(
        ActorContext actor,
        Guid knowledgeBaseId,
        Guid? conversationId,
        string query,
        string answer,
        int sourceCount,
        string answerModel,
        string agentMode,
        long latencyMs,
        IReadOnlyList<object> trace,
        string status,
        CancellationToken ct = default)
    {
        var tenantId = await ResolveTenantIdAsync(knowledgeBaseId, ct);
        var inputTokens = EstimateTokens(query) + trace.Count * 12;
        var outputTokens = EstimateTokens(answer);
        var cost = await runtimeProfiles.EstimateCostAsync(tenantId, answerModel, inputTokens, outputTokens, ct);
        var entity = new RagRunEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, KnowledgeBaseId = knowledgeBaseId, ConversationId = conversationId,
            UserId = actor.UserId, ApiKeyId = actor.ApiKeyId, Query = query, Answer = answer, Status = status,
            AnswerModel = answerModel, AgentMode = agentMode, SourceCount = sourceCount,
            EstimatedInputTokens = inputTokens, EstimatedOutputTokens = outputTokens, EstimatedCostUsd = cost,
            LatencyMs = latencyMs, TraceJson = JsonSerializer.Serialize(trace, WebJson), CreatedAt = DateTimeOffset.UtcNow
        };
        db.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task WriteAuditAsync(
        ActorContext actor,
        Guid? tenantId,
        string action,
        string resourceType,
        string? resourceId,
        string summary,
        int statusCode,
        string? ipAddress,
        string? userAgent,
        object? details = null,
        CancellationToken ct = default)
    {
        db.Add(new AuditLogEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = actor.UserId, ApiKeyId = actor.ApiKeyId,
            Action = action, ResourceType = resourceType, ResourceId = resourceId, Summary = summary,
            StatusCode = statusCode, IpAddress = ipAddress, UserAgent = userAgent,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details, WebJson), CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    private static int EstimateTokens(string value) => string.IsNullOrEmpty(value) ? 0 : Math.Max(1, (int)Math.Ceiling(value.Length / 4d));
}
