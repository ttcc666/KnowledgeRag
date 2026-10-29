using System.Net;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Application.Security;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.AI;
using KnowledgeRag.Infrastructure.Configuration;
using KnowledgeRag.Infrastructure.Governance;
using KnowledgeRag.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.Tenancy;

public sealed class TenantAiProviderService(
    AppDbContext db,
    TenantAccessService access,
    AiCredentialProtector credentialProtector,
    AiProviderClientFactory clientFactory,
    IOptions<AiOptions> aiOptions)
{
    private readonly AiOptions _ai = aiOptions.Value;

    private static readonly AiProviderDescriptor[] ProviderCatalog =
    [
        new(AiProviderTypes.OpenAiCompatible, "OpenAI Compatible", "OpenAI API 兼容端点，可接 OpenAI、DeepSeek、Qwen、vLLM、LiteLLM、OpenRouter 等。", true)
    ];

    public IReadOnlyList<AiProviderDescriptor> ListProviderCatalog() => ProviderCatalog;

    public async Task<IReadOnlyList<AiProviderConnectionSummary>> ListConnectionsAsync(ActorContext actor, Guid tenantId, CancellationToken ct = default)
    {
        await access.RequireAsync(actor, tenantId, TenantRole.Member, ct);
        var rows = await db.Set<AiProviderConnectionEntity>().AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(ct);
        return rows.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(ToSummary).ToList();
    }

    public async Task<AiProviderConnectionSummary> SaveConnectionAsync(ActorContext actor, Guid tenantId, SaveAiProviderConnectionRequest request, CancellationToken ct = default)
    {
        await access.RequireAsync(actor, tenantId, TenantRole.Admin, ct);
        var name = Required(request.Name, 200, "连接名称");
        var providerType = NormalizeProviderType(request.ProviderType);
        var baseUrl = NormalizeTenantBaseUrl(request.BaseUrl);
        var now = DateTimeOffset.UtcNow;
        AiProviderConnectionEntity entity;

        if (request.Id is Guid id)
        {
            entity = await db.Set<AiProviderConnectionEntity>().SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct)
                ?? throw new KeyNotFoundException("AI Provider Connection 不存在。");
            var activeIndexUsesConnection = await db.KnowledgeBaseIndexGenerations.AnyAsync(x =>
                x.ProviderConnectionId == id &&
                (x.Status == KnowledgeBaseIndexGenerationStatuses.Active || x.Status == KnowledgeBaseIndexGenerationStatuses.Building), ct);
            if (activeIndexUsesConnection && (!request.IsEnabled || !entity.BaseUrl.Equals(baseUrl, StringComparison.Ordinal) ||
                !entity.ProviderType.Equals(providerType, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("该 Provider Connection 正在服务活动知识库索引。请先切换并完成 Embedding 重建后，再停用或更改 Provider / Base URL；仅轮换凭据不受限制。");
            entity.Name = name;
            entity.ProviderType = providerType;
            entity.BaseUrl = baseUrl;
            entity.IsEnabled = request.IsEnabled;
            entity.Revision++;
            entity.UpdatedAt = now;
            if (!string.IsNullOrWhiteSpace(request.Credential))
                entity.EncryptedCredential = credentialProtector.Protect(request.Credential);
        }
        else
        {
            entity = new AiProviderConnectionEntity
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Name = name, ProviderType = providerType, BaseUrl = baseUrl,
                EncryptedCredential = string.IsNullOrWhiteSpace(request.Credential) ? string.Empty : credentialProtector.Protect(request.Credential),
                ConfigJson = "{}", IsEnabled = request.IsEnabled, Revision = 1, CreatedByUserId = actor.UserId, CreatedAt = now, UpdatedAt = now
            };
            db.Add(entity);
        }

        await db.SaveChangesAsync(ct);
        return ToSummary(entity);
    }

    public async Task DeleteConnectionAsync(ActorContext actor, Guid tenantId, Guid connectionId, CancellationToken ct = default)
    {
        await access.RequireAsync(actor, tenantId, TenantRole.Admin, ct);
        var entity = await db.Set<AiProviderConnectionEntity>().SingleOrDefaultAsync(x => x.Id == connectionId && x.TenantId == tenantId, ct);
        if (entity is null) return;
        if (await db.KnowledgeBaseIndexGenerations.AnyAsync(x => x.ProviderConnectionId == connectionId &&
            (x.Status == KnowledgeBaseIndexGenerationStatuses.Building || x.Status == KnowledgeBaseIndexGenerationStatuses.Active), ct))
            throw new InvalidOperationException("该 Provider Connection 正在被活动或构建中的知识库索引使用，请先完成或取消索引重建。");
        if (await db.Set<AiModelDeploymentEntity>().AnyAsync(x => x.ConnectionId == connectionId, ct))
            throw new InvalidOperationException("该连接仍有模型 Deployment，请先删除或迁移 Deployment。");
        db.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AiConnectionTestResult> TestConnectionAsync(ActorContext actor, Guid tenantId, Guid connectionId, CancellationToken ct = default)
    {
        await access.RequireAsync(actor, tenantId, TenantRole.Admin, ct);
        var connection = await db.Set<AiProviderConnectionEntity>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == connectionId && x.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("AI Provider Connection 不存在。");
        if (!connection.IsEnabled) throw new InvalidOperationException("AI Provider Connection 已停用。");

        var endpoint = ToEndpoint(connection, null, "connection-test");
        using var client = clientFactory.CreateHttpClient(endpoint);
        using var request = clientFactory.CreateJsonRequest(endpoint, HttpMethod.Get, "models");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.IsSuccessStatusCode) return new AiConnectionTestResult(true, "连接验证成功。");
        var raw = await response.Content.ReadAsStringAsync(ct);
        var detail = raw.Length <= 300 ? raw : raw[..300];
        return new AiConnectionTestResult(false, $"Provider 返回 {(int)response.StatusCode} {response.StatusCode}: {detail}");
    }

    public async Task<IReadOnlyList<AiModelDeploymentSummary>> ListDeploymentsAsync(ActorContext actor, Guid tenantId, CancellationToken ct = default)
    {
        await access.RequireAsync(actor, tenantId, TenantRole.Member, ct);
        var rows = await (from deployment in db.Set<AiModelDeploymentEntity>().AsNoTracking()
                          join connection in db.Set<AiProviderConnectionEntity>().AsNoTracking() on deployment.ConnectionId equals connection.Id
                          where deployment.TenantId == tenantId && connection.TenantId == tenantId
                          select new AiModelDeploymentSummary(deployment.Id, deployment.TenantId, deployment.ConnectionId, connection.Name,
                              deployment.DisplayName, deployment.RemoteModelName, deployment.ModelType, deployment.EmbeddingDimensions,
                              deployment.IsEnabled, deployment.UpdatedAt, deployment.FallbackDeploymentId, deployment.HealthStatus,
                              deployment.LastHealthCheckedAt, deployment.LastHealthError)).ToListAsync(ct);
        return rows.OrderBy(x => x.ModelType).ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<AiModelDeploymentSummary> SaveDeploymentAsync(ActorContext actor, Guid tenantId, SaveAiModelDeploymentRequest request, CancellationToken ct = default)
    {
        await access.RequireAsync(actor, tenantId, TenantRole.Admin, ct);
        var connection = await db.Set<AiProviderConnectionEntity>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.ConnectionId && x.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException("AI Provider Connection 不属于当前租户。");
        var displayName = Required(request.DisplayName, 200, "Deployment 名称");
        var remoteModel = Required(request.RemoteModelName, 200, "远端模型名");
        var modelType = NormalizeModelType(request.ModelType);
        if (modelType == AiModelTypes.Embedding && request.EmbeddingDimensions is <= 0)
            throw new InvalidOperationException("Embedding Deployment 必须提供正整数维度。");
        if (modelType != AiModelTypes.Embedding && request.EmbeddingDimensions.HasValue)
            throw new InvalidOperationException("只有 Embedding Deployment 可以配置维度。");

        var now = DateTimeOffset.UtcNow;
        var entityId = request.Id ?? Guid.NewGuid();
        await ValidateFallbackAsync(tenantId, entityId, request.FallbackDeploymentId, modelType, ct);
        AiModelDeploymentEntity entity;
        if (request.Id is Guid id)
        {
            entity = await db.Set<AiModelDeploymentEntity>().SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct)
                ?? throw new KeyNotFoundException("Model Deployment 不存在。");
            var healthTargetChanged = entity.ConnectionId != request.ConnectionId ||
                                      !entity.RemoteModelName.Equals(remoteModel, StringComparison.Ordinal) ||
                                      !entity.ModelType.Equals(modelType, StringComparison.OrdinalIgnoreCase) ||
                                      entity.IsEnabled != request.IsEnabled;
            entity.ConnectionId = request.ConnectionId;
            entity.DisplayName = displayName;
            entity.RemoteModelName = remoteModel;
            entity.ModelType = modelType;
            entity.EmbeddingDimensions = request.EmbeddingDimensions;
            entity.FallbackDeploymentId = request.FallbackDeploymentId;
            if (healthTargetChanged)
            {
                entity.HealthStatus = AiDeploymentHealthStatuses.Unknown;
                entity.LastHealthCheckedAt = null;
                entity.LastHealthError = null;
            }
            entity.IsEnabled = request.IsEnabled;
            entity.UpdatedAt = now;
        }
        else
        {
            entity = new AiModelDeploymentEntity
            {
                Id = entityId, TenantId = tenantId, ConnectionId = request.ConnectionId, DisplayName = displayName,
                RemoteModelName = remoteModel, ModelType = modelType, EmbeddingDimensions = request.EmbeddingDimensions,
                FallbackDeploymentId = request.FallbackDeploymentId, HealthStatus = AiDeploymentHealthStatuses.Unknown,
                IsEnabled = request.IsEnabled, CreatedAt = now, UpdatedAt = now
            };
            db.Add(entity);
        }
        db.AuditLogs.Add(new AuditLogEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = actor.UserId, ApiKeyId = actor.ApiKeyId,
            Action = "ai.deployment.saved", ResourceType = "ai-deployment", ResourceId = entity.Id.ToString(),
            Summary = $"Saved {entity.ModelType} deployment {entity.DisplayName}.",
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { entity.ConnectionId, entity.FallbackDeploymentId, entity.IsEnabled }),
            StatusCode = 200, CreatedAt = now
        });
        await db.SaveChangesAsync(ct);
        return new AiModelDeploymentSummary(entity.Id, tenantId, entity.ConnectionId, connection.Name, entity.DisplayName,
            entity.RemoteModelName, entity.ModelType, entity.EmbeddingDimensions, entity.IsEnabled, entity.UpdatedAt,
            entity.FallbackDeploymentId, entity.HealthStatus, entity.LastHealthCheckedAt, entity.LastHealthError);
    }

    public async Task DeleteDeploymentAsync(ActorContext actor, Guid tenantId, Guid deploymentId, CancellationToken ct = default)
    {
        await access.RequireAsync(actor, tenantId, TenantRole.Admin, ct);
        var entity = await db.Set<AiModelDeploymentEntity>().SingleOrDefaultAsync(x => x.Id == deploymentId && x.TenantId == tenantId, ct);
        if (entity is null) return;
        var defaults = await db.Set<TenantAiDefaultsEntity>().SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);
        if (defaults is not null && (defaults.DefaultChatDeploymentId == deploymentId || defaults.DefaultEmbeddingDeploymentId == deploymentId || defaults.DefaultRerankerDeploymentId == deploymentId))
            throw new InvalidOperationException("该 Deployment 正在被租户默认配置使用，请先修改默认模型。");
        if (await db.AiModelDeployments.AnyAsync(x => x.FallbackDeploymentId == deploymentId, ct))
            throw new InvalidOperationException("该 Deployment 正在被其他 Deployment 作为 fallback 使用，请先修改 fallback 链。");
        if (await db.KnowledgeBaseRagProfiles.AnyAsync(x => x.PlannerDeploymentId == deploymentId || x.AnswerDeploymentId == deploymentId ||
            x.ReviewerDeploymentId == deploymentId || x.RerankerDeploymentId == deploymentId, ct))
            throw new InvalidOperationException("该 Deployment 正在被知识库 RAG Profile 使用，请先修改角色路由。");
        if (await db.ModelPrices.AnyAsync(x => x.DeploymentId == deploymentId, ct))
            throw new InvalidOperationException("该 Deployment 已绑定价格记录；为保持成本身份不可变，请停用而不是删除。");
        if (await db.RagRuns.AnyAsync(x => x.AnswerDeploymentId == deploymentId, ct))
            throw new InvalidOperationException("该 Deployment 已出现在历史 RAG Run；为保持运行账本身份不可变，请停用而不是删除。");
        db.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task<TenantAiDefaultsSummary> GetDefaultsAsync(ActorContext actor, Guid tenantId, CancellationToken ct = default)
    {
        await access.RequireAsync(actor, tenantId, TenantRole.Member, ct);
        var entity = await db.Set<TenantAiDefaultsEntity>().AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);
        return entity is null
            ? new TenantAiDefaultsSummary(tenantId, null, null, null, null)
            : new TenantAiDefaultsSummary(tenantId, entity.DefaultChatDeploymentId, entity.DefaultEmbeddingDeploymentId, entity.DefaultRerankerDeploymentId, entity.UpdatedAt);
    }

    public async Task<TenantAiDefaultsSummary> SaveDefaultsAsync(ActorContext actor, Guid tenantId, SaveTenantAiDefaultsRequest request, CancellationToken ct = default)
    {
        await access.RequireAsync(actor, tenantId, TenantRole.Admin, ct);
        await ValidateDefaultAsync(tenantId, request.DefaultChatDeploymentId, AiModelTypes.Chat, ct);
        await ValidateDefaultAsync(tenantId, request.DefaultEmbeddingDeploymentId, AiModelTypes.Embedding, ct);
        await ValidateDefaultAsync(tenantId, request.DefaultRerankerDeploymentId, AiModelTypes.Rerank, ct);
        var entity = await db.Set<TenantAiDefaultsEntity>().SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);
        var now = DateTimeOffset.UtcNow;
        if (entity is null)
        {
            entity = new TenantAiDefaultsEntity { TenantId = tenantId };
            db.Add(entity);
        }
        entity.DefaultChatDeploymentId = request.DefaultChatDeploymentId;
        entity.DefaultEmbeddingDeploymentId = request.DefaultEmbeddingDeploymentId;
        entity.DefaultRerankerDeploymentId = request.DefaultRerankerDeploymentId;
        entity.UpdatedByUserId = actor.UserId;
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return new TenantAiDefaultsSummary(tenantId, entity.DefaultChatDeploymentId, entity.DefaultEmbeddingDeploymentId, entity.DefaultRerankerDeploymentId, entity.UpdatedAt);
    }

    public async Task<ResolvedAiEndpoint?> ResolveDefaultEndpointAsync(Guid tenantId, string modelType, CancellationToken ct = default)
    {
        var defaults = await db.Set<TenantAiDefaultsEntity>().AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);
        var deploymentId = modelType switch
        {
            AiModelTypes.Chat => defaults?.DefaultChatDeploymentId,
            AiModelTypes.Embedding => defaults?.DefaultEmbeddingDeploymentId,
            AiModelTypes.Rerank => defaults?.DefaultRerankerDeploymentId,
            _ => throw new InvalidOperationException($"不支持的模型类型：{modelType}。")
        };
        return deploymentId.HasValue ? await ResolveEndpointWithFallbackAsync(tenantId, deploymentId.Value, modelType, ct) : null;
    }

    public async Task<AiDeploymentHealthResult> TestDeploymentHealthAsync(
        ActorContext actor,
        Guid tenantId,
        Guid deploymentId,
        CancellationToken ct = default)
    {
        await access.RequireAsync(actor, tenantId, TenantRole.Admin, ct);
        var row = await (from deployment in db.AiModelDeployments
                         join connection in db.AiProviderConnections.AsNoTracking() on deployment.ConnectionId equals connection.Id
                         where deployment.Id == deploymentId && deployment.TenantId == tenantId && connection.TenantId == tenantId
                         select new { Deployment = deployment, Connection = connection }).SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Model Deployment 不存在。");
        var checkedAt = DateTimeOffset.UtcNow;
        var success = false;
        string message;
        try
        {
            if (!row.Deployment.IsEnabled || !row.Connection.IsEnabled)
                throw new InvalidOperationException("Deployment 或 Provider Connection 已停用。");
            var endpoint = ToEndpoint(row.Connection, row.Deployment.Id, row.Deployment.RemoteModelName);
            using var client = clientFactory.CreateHttpClient(endpoint);
            using var request = clientFactory.CreateJsonRequest(endpoint, HttpMethod.Get, "models");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            success = response.IsSuccessStatusCode;
            message = success ? "Deployment health check 成功。" : $"Provider 返回 {(int)response.StatusCode} {response.StatusCode}。";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            message = ex.Message.Length <= 300 ? ex.Message : ex.Message[..300];
        }

        row.Deployment.HealthStatus = success ? AiDeploymentHealthStatuses.Healthy : AiDeploymentHealthStatuses.Unhealthy;
        row.Deployment.LastHealthCheckedAt = checkedAt;
        row.Deployment.LastHealthError = success ? null : message;
        db.AuditLogs.Add(new AuditLogEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = actor.UserId, ApiKeyId = actor.ApiKeyId,
            Action = "ai.deployment.health", ResourceType = "ai-deployment", ResourceId = deploymentId.ToString(),
            Summary = success ? "Deployment health check passed." : "Deployment health check failed.",
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { status = row.Deployment.HealthStatus }),
            StatusCode = success ? 200 : 503, CreatedAt = checkedAt
        });
        await db.SaveChangesAsync(ct);
        return new AiDeploymentHealthResult(deploymentId, success, row.Deployment.HealthStatus, message, checkedAt);
    }

    public async Task<EmbeddingEndpointResolution> ResolveEmbeddingTargetAsync(Guid knowledgeBaseId, CancellationToken ct = default)
    {
        var knowledgeBase = await db.KnowledgeBases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == knowledgeBaseId, ct)
            ?? throw new KeyNotFoundException("知识库不存在。");
        var defaults = await db.Set<TenantAiDefaultsEntity>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == knowledgeBase.TenantId, ct);
        if (defaults?.DefaultEmbeddingDeploymentId is not Guid deploymentId)
            return PlatformEmbeddingResolution();

        var row = await (from deployment in db.Set<AiModelDeploymentEntity>().AsNoTracking()
                         join connection in db.Set<AiProviderConnectionEntity>().AsNoTracking() on deployment.ConnectionId equals connection.Id
                         where deployment.Id == deploymentId && deployment.TenantId == knowledgeBase.TenantId &&
                               connection.TenantId == knowledgeBase.TenantId
                         select new { Deployment = deployment, Connection = connection }).SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("租户默认 Embedding Deployment 不存在或不属于当前租户。");
        if (!row.Deployment.IsEnabled || !row.Connection.IsEnabled)
            throw new InvalidOperationException("租户默认 Embedding Deployment 或 Provider Connection 已停用。");
        if (!row.Deployment.ModelType.Equals(AiModelTypes.Embedding, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("租户默认 Deployment 不是 Embedding 类型。");
        var dimensions = row.Deployment.EmbeddingDimensions.GetValueOrDefault();
        if (dimensions <= 0)
            throw new InvalidOperationException("租户默认 Embedding Deployment 缺少有效维度配置。");

        return new EmbeddingEndpointResolution(
            ToEndpoint(row.Connection, row.Deployment.Id, row.Deployment.RemoteModelName),
            dimensions,
            true,
            row.Deployment.DisplayName,
            row.Deployment.UpdatedAt);
    }

    public async Task<EmbeddingEndpointResolution> ResolveEmbeddingEndpointForGenerationAsync(
        Guid knowledgeBaseId,
        Guid indexGenerationId,
        CancellationToken ct = default)
    {
        if (indexGenerationId == Guid.Empty) return PlatformEmbeddingResolution();
        var generation = await db.KnowledgeBaseIndexGenerations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == indexGenerationId && x.KnowledgeBaseId == knowledgeBaseId, ct)
            ?? throw new InvalidOperationException("活动 Embedding 索引代次不存在。");

        if (generation.ProviderConnectionId is not Guid connectionId)
        {
            return new EmbeddingEndpointResolution(
                new ResolvedAiEndpoint(generation.EmbeddingDeploymentId, null, generation.ProviderType,
                    generation.BaseUrl, _ai.ApiKey, generation.ModelName, 0, false),
                generation.Dimensions,
                _ai.SendEmbeddingDimensions,
                generation.DeploymentDisplayName,
                generation.DeploymentUpdatedAt);
        }

        var connection = await db.Set<AiProviderConnectionEntity>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == connectionId && x.TenantId == generation.TenantId, ct)
            ?? throw new InvalidOperationException("活动 Embedding 索引引用的 Provider Connection 已不存在。");
        if (!connection.IsEnabled)
            throw new InvalidOperationException("活动 Embedding 索引使用的 Provider Connection 已停用。");
        if (!connection.ProviderType.Equals(generation.ProviderType, StringComparison.OrdinalIgnoreCase) ||
            !connection.BaseUrl.Equals(generation.BaseUrl, StringComparison.Ordinal))
            throw new InvalidOperationException("活动 Embedding 索引的 Provider / Base URL 已变化。请恢复原配置或完成新的索引重建。");

        return new EmbeddingEndpointResolution(
            ToEndpoint(connection, generation.EmbeddingDeploymentId, generation.ModelName),
            generation.Dimensions,
            true,
            generation.DeploymentDisplayName,
            generation.DeploymentUpdatedAt);
    }

    private EmbeddingEndpointResolution PlatformEmbeddingResolution()
        => new(
            new ResolvedAiEndpoint(null, null, AiProviderTypes.OpenAiCompatible, _ai.BaseUrl, _ai.ApiKey,
                _ai.EmbeddingModel, 0, false),
            _ai.EmbeddingDimensions,
            _ai.SendEmbeddingDimensions,
            "Platform default",
            null);

    public async Task<ResolvedAiEndpoint> ResolveEndpointWithFallbackAsync(Guid tenantId, Guid deploymentId, string expectedType, CancellationToken ct = default)
    {
        var visited = new HashSet<Guid>();
        var currentId = deploymentId;
        for (var depth = 0; depth < 16; depth++)
        {
            if (!visited.Add(currentId))
                throw new InvalidOperationException("Model Deployment fallback 链存在循环。");
            var row = await (from deployment in db.AiModelDeployments.AsNoTracking()
                             join connection in db.AiProviderConnections.AsNoTracking() on deployment.ConnectionId equals connection.Id
                             where deployment.Id == currentId && deployment.TenantId == tenantId && connection.TenantId == tenantId
                             select new { Deployment = deployment, Connection = connection }).SingleOrDefaultAsync(ct)
                ?? throw new InvalidOperationException("Model Deployment 不存在或不属于当前租户。");
            if (!row.Deployment.ModelType.Equals(expectedType, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Model Deployment 类型应为 {expectedType}。");

            var available = row.Deployment.IsEnabled && row.Connection.IsEnabled &&
                            !row.Deployment.HealthStatus.Equals(AiDeploymentHealthStatuses.Unhealthy, StringComparison.OrdinalIgnoreCase);
            if (available) return ToEndpoint(row.Connection, row.Deployment.Id, row.Deployment.RemoteModelName);
            if (row.Deployment.FallbackDeploymentId is not Guid fallbackId)
                throw new InvalidOperationException($"Deployment '{row.Deployment.DisplayName}' 当前不可用，且未配置 fallback。");
            currentId = fallbackId;
        }
        throw new InvalidOperationException("Model Deployment fallback 链超过允许深度。");
    }

    public async Task<ResolvedAiEndpoint> ResolveEndpointAsync(Guid tenantId, Guid deploymentId, string expectedType, CancellationToken ct = default)
    {
        var row = await (from deployment in db.Set<AiModelDeploymentEntity>().AsNoTracking()
                         join connection in db.Set<AiProviderConnectionEntity>().AsNoTracking() on deployment.ConnectionId equals connection.Id
                         where deployment.Id == deploymentId && deployment.TenantId == tenantId && connection.TenantId == tenantId
                         select new { Deployment = deployment, Connection = connection }).SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Model Deployment 不存在或不属于当前租户。");
        if (!row.Deployment.IsEnabled || !row.Connection.IsEnabled)
            throw new InvalidOperationException("Model Deployment 或 Provider Connection 已停用。");
        if (!row.Deployment.ModelType.Equals(expectedType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Model Deployment 类型应为 {expectedType}。");
        return ToEndpoint(row.Connection, row.Deployment.Id, row.Deployment.RemoteModelName);
    }

    private ResolvedAiEndpoint ToEndpoint(AiProviderConnectionEntity connection, Guid? deploymentId, string model)
        => new(deploymentId, connection.Id, connection.ProviderType, connection.BaseUrl,
            string.IsNullOrWhiteSpace(connection.EncryptedCredential) ? string.Empty : credentialProtector.Unprotect(connection.EncryptedCredential),
            model, connection.Revision, true);

    private async Task ValidateDefaultAsync(Guid tenantId, Guid? deploymentId, string modelType, CancellationToken ct)
    {
        if (!deploymentId.HasValue) return;
        _ = await ResolveEndpointAsync(tenantId, deploymentId.Value, modelType, ct);
    }

    private async Task ValidateFallbackAsync(Guid tenantId, Guid deploymentId, Guid? fallbackDeploymentId, string modelType, CancellationToken ct)
    {
        if (!fallbackDeploymentId.HasValue) return;
        var currentId = fallbackDeploymentId.Value;
        var visited = new HashSet<Guid> { deploymentId };
        for (var depth = 0; depth < 16; depth++)
        {
            if (!visited.Add(currentId)) throw new InvalidOperationException("Model Deployment fallback 不能形成循环。");
            var row = await db.AiModelDeployments.AsNoTracking()
                .Where(x => x.Id == currentId && x.TenantId == tenantId)
                .Select(x => new { x.Id, x.ModelType, x.FallbackDeploymentId })
                .SingleOrDefaultAsync(ct)
                ?? throw new InvalidOperationException("Fallback Deployment 不存在或不属于当前租户。");
            if (!row.ModelType.Equals(modelType, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fallback Deployment 必须与主 Deployment 类型一致。");
            if (row.FallbackDeploymentId is not Guid next) return;
            currentId = next;
        }
        throw new InvalidOperationException("Model Deployment fallback 链超过允许深度。");
    }

    private static AiProviderConnectionSummary ToSummary(AiProviderConnectionEntity x)
        => new(x.Id, x.TenantId, x.Name, x.ProviderType, x.BaseUrl, !string.IsNullOrWhiteSpace(x.EncryptedCredential), x.IsEnabled, x.Revision, x.UpdatedAt);

    private static string NormalizeProviderType(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized == AiProviderTypes.OpenAiCompatible
            ? normalized
            : throw new InvalidOperationException($"不支持的 AI Provider：{value}。");
    }

    private static string NormalizeModelType(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is AiModelTypes.Chat or AiModelTypes.Embedding or AiModelTypes.Rerank
            ? normalized
            : throw new InvalidOperationException("模型类型必须是 chat、embedding 或 rerank。");
    }

    private static string NormalizeTenantBaseUrl(string value)
    {
        var uri = WebhookUrlPolicy.ValidateUrl((value ?? string.Empty).Trim().TrimEnd('/') + "/");
        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("租户管理的 AI Provider Base URL 必须使用 HTTPS。");
        return uri.ToString().TrimEnd('/');
    }

    private static string Required(string value, int maxLength, string name)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is < 1 || normalized.Length > maxLength)
            throw new InvalidOperationException($"{name}长度必须在 1-{maxLength} 个字符之间。");
        return normalized;
    }
}
