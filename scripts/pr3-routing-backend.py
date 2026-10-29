from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected one match, found {count}")
    return text.replace(old, new, 1)

# Persistence mappings.
p = Path('src/KnowledgeRag.Infrastructure/Persistence/AppDbContext.cs')
s = p.read_text()
s = replace_once(s,
'''            entity.Property(x => x.ModelType).HasMaxLength(32);
            entity.HasIndex(x => new { x.TenantId, x.ModelType, x.IsEnabled });
            entity.HasIndex(x => new { x.TenantId, x.ConnectionId, x.DisplayName }).IsUnique();
            entity.HasOne<TenantEntity>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AiProviderConnectionEntity>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);''',
'''            entity.Property(x => x.ModelType).HasMaxLength(32);
            entity.Property(x => x.HealthStatus).HasMaxLength(32);
            entity.Property(x => x.LastHealthError).HasMaxLength(1000);
            entity.HasIndex(x => new { x.TenantId, x.ModelType, x.IsEnabled });
            entity.HasIndex(x => new { x.TenantId, x.HealthStatus });
            entity.HasIndex(x => new { x.TenantId, x.ConnectionId, x.DisplayName }).IsUnique();
            entity.HasOne<TenantEntity>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AiProviderConnectionEntity>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AiModelDeploymentEntity>().WithMany().HasForeignKey(x => x.FallbackDeploymentId).OnDelete(DeleteBehavior.Restrict);''',
'ai deployment mapping')
s = replace_once(s,
'''            entity.Property(x => x.AgentMode).HasMaxLength(64);
            entity.HasOne<KnowledgeBaseEntity>().WithOne().HasForeignKey<KnowledgeBaseRagProfileEntity>(x => x.KnowledgeBaseId).OnDelete(DeleteBehavior.Cascade);''',
'''            entity.Property(x => x.AgentMode).HasMaxLength(64);
            entity.HasOne<KnowledgeBaseEntity>().WithOne().HasForeignKey<KnowledgeBaseRagProfileEntity>(x => x.KnowledgeBaseId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AiModelDeploymentEntity>().WithMany().HasForeignKey(x => x.PlannerDeploymentId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<AiModelDeploymentEntity>().WithMany().HasForeignKey(x => x.AnswerDeploymentId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<AiModelDeploymentEntity>().WithMany().HasForeignKey(x => x.ReviewerDeploymentId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<AiModelDeploymentEntity>().WithMany().HasForeignKey(x => x.RerankerDeploymentId).OnDelete(DeleteBehavior.SetNull);''',
'rag profile deployment mapping')
s = replace_once(s,
'''            entity.HasOne<KnowledgeBaseEntity>().WithMany().HasForeignKey(x => x.KnowledgeBaseId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ConversationEntity>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.SetNull);''',
'''            entity.HasOne<KnowledgeBaseEntity>().WithMany().HasForeignKey(x => x.KnowledgeBaseId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ConversationEntity>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<AiModelDeploymentEntity>().WithMany().HasForeignKey(x => x.AnswerDeploymentId).OnDelete(DeleteBehavior.SetNull);''',
'rag run deployment mapping')
s = replace_once(s,
'''            entity.Property(x => x.Model).HasMaxLength(200);
            entity.HasIndex(x => new { x.TenantId, x.Model }).IsUnique();
            entity.HasOne<TenantEntity>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);''',
'''            entity.Property(x => x.Model).HasMaxLength(200);
            entity.HasIndex(x => new { x.TenantId, x.Model });
            entity.HasIndex(x => new { x.TenantId, x.DeploymentId }).IsUnique();
            entity.HasOne<TenantEntity>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AiModelDeploymentEntity>().WithMany().HasForeignKey(x => x.DeploymentId).OnDelete(DeleteBehavior.SetNull);''',
'model price deployment mapping')
p.write_text(s)

# Tenant provider deployment routing, health and fallback.
p = Path('src/KnowledgeRag.Infrastructure/Tenancy/TenantAiProviderService.cs')
s = p.read_text()
s = replace_once(s,
'''                          select new AiModelDeploymentSummary(deployment.Id, deployment.TenantId, deployment.ConnectionId, connection.Name,
                              deployment.DisplayName, deployment.RemoteModelName, deployment.ModelType, deployment.EmbeddingDimensions,
                              deployment.IsEnabled, deployment.UpdatedAt)).ToListAsync(ct);''',
'''                          select new AiModelDeploymentSummary(deployment.Id, deployment.TenantId, deployment.ConnectionId, connection.Name,
                              deployment.DisplayName, deployment.RemoteModelName, deployment.ModelType, deployment.EmbeddingDimensions,
                              deployment.IsEnabled, deployment.UpdatedAt, deployment.FallbackDeploymentId, deployment.HealthStatus,
                              deployment.LastHealthCheckedAt, deployment.LastHealthError)).ToListAsync(ct);''',
'list deployment summary')
s = replace_once(s,
'''        var now = DateTimeOffset.UtcNow;
        AiModelDeploymentEntity entity;
        if (request.Id is Guid id)''',
'''        var now = DateTimeOffset.UtcNow;
        var entityId = request.Id ?? Guid.NewGuid();
        await ValidateFallbackAsync(tenantId, entityId, request.FallbackDeploymentId, modelType, ct);
        AiModelDeploymentEntity entity;
        if (request.Id is Guid id)''',
'deployment validate fallback')
s = replace_once(s,
'''            entity.EmbeddingDimensions = request.EmbeddingDimensions;
            entity.IsEnabled = request.IsEnabled;
            entity.UpdatedAt = now;''',
'''            entity.EmbeddingDimensions = request.EmbeddingDimensions;
            entity.FallbackDeploymentId = request.FallbackDeploymentId;
            entity.HealthStatus = AiDeploymentHealthStatuses.Unknown;
            entity.LastHealthCheckedAt = null;
            entity.LastHealthError = null;
            entity.IsEnabled = request.IsEnabled;
            entity.UpdatedAt = now;''',
'edit deployment routing metadata')
s = replace_once(s,
'''                Id = Guid.NewGuid(), TenantId = tenantId, ConnectionId = request.ConnectionId, DisplayName = displayName,
                RemoteModelName = remoteModel, ModelType = modelType, EmbeddingDimensions = request.EmbeddingDimensions,
                IsEnabled = request.IsEnabled, CreatedAt = now, UpdatedAt = now''',
'''                Id = entityId, TenantId = tenantId, ConnectionId = request.ConnectionId, DisplayName = displayName,
                RemoteModelName = remoteModel, ModelType = modelType, EmbeddingDimensions = request.EmbeddingDimensions,
                FallbackDeploymentId = request.FallbackDeploymentId, HealthStatus = AiDeploymentHealthStatuses.Unknown,
                IsEnabled = request.IsEnabled, CreatedAt = now, UpdatedAt = now''',
'new deployment routing metadata')
s = replace_once(s,
'''        await db.SaveChangesAsync(ct);
        return new AiModelDeploymentSummary(entity.Id, tenantId, entity.ConnectionId, connection.Name, entity.DisplayName,
            entity.RemoteModelName, entity.ModelType, entity.EmbeddingDimensions, entity.IsEnabled, entity.UpdatedAt);''',
'''        db.AuditLogs.Add(new AuditLogEntity
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
            entity.FallbackDeploymentId, entity.HealthStatus, entity.LastHealthCheckedAt, entity.LastHealthError);''',
'save deployment summary and audit')
s = replace_once(s,
'''        var defaults = await db.Set<TenantAiDefaultsEntity>().SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);
        if (defaults is not null && (defaults.DefaultChatDeploymentId == deploymentId || defaults.DefaultEmbeddingDeploymentId == deploymentId || defaults.DefaultRerankerDeploymentId == deploymentId))
            throw new InvalidOperationException("该 Deployment 正在被租户默认配置使用，请先修改默认模型。");
        db.Remove(entity);''',
'''        var defaults = await db.Set<TenantAiDefaultsEntity>().SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);
        if (defaults is not null && (defaults.DefaultChatDeploymentId == deploymentId || defaults.DefaultEmbeddingDeploymentId == deploymentId || defaults.DefaultRerankerDeploymentId == deploymentId))
            throw new InvalidOperationException("该 Deployment 正在被租户默认配置使用，请先修改默认模型。");
        if (await db.AiModelDeployments.AnyAsync(x => x.FallbackDeploymentId == deploymentId, ct))
            throw new InvalidOperationException("该 Deployment 正在被其他 Deployment 作为 fallback 使用，请先修改 fallback 链。");
        if (await db.KnowledgeBaseRagProfiles.AnyAsync(x => x.PlannerDeploymentId == deploymentId || x.AnswerDeploymentId == deploymentId ||
            x.ReviewerDeploymentId == deploymentId || x.RerankerDeploymentId == deploymentId, ct))
            throw new InvalidOperationException("该 Deployment 正在被知识库 RAG Profile 使用，请先修改角色路由。");
        db.Remove(entity);''',
'delete deployment guards')
s = replace_once(s,
'''        return deploymentId.HasValue ? await ResolveEndpointAsync(tenantId, deploymentId.Value, modelType, ct) : null;
    }

    public async Task<EmbeddingEndpointResolution> ResolveEmbeddingTargetAsync''',
'''        return deploymentId.HasValue ? await ResolveEndpointWithFallbackAsync(tenantId, deploymentId.Value, modelType, ct) : null;
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

    public async Task<EmbeddingEndpointResolution> ResolveEmbeddingTargetAsync''',
'deployment health API')
s = replace_once(s,
'''    public async Task<ResolvedAiEndpoint> ResolveEndpointAsync(Guid tenantId, Guid deploymentId, string expectedType, CancellationToken ct = default)
    {''',
'''    public async Task<ResolvedAiEndpoint> ResolveEndpointWithFallbackAsync(Guid tenantId, Guid deploymentId, string expectedType, CancellationToken ct = default)
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
    {''',
'fallback resolver')
s = replace_once(s,
'''    private async Task ValidateDefaultAsync(Guid tenantId, Guid? deploymentId, string modelType, CancellationToken ct)
    {
        if (!deploymentId.HasValue) return;
        _ = await ResolveEndpointAsync(tenantId, deploymentId.Value, modelType, ct);
    }
''',
'''    private async Task ValidateDefaultAsync(Guid tenantId, Guid? deploymentId, string modelType, CancellationToken ct)
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
''',
'fallback validation helper')
p.write_text(s)

# Governance profile and deployment-level pricing.
p = Path('src/KnowledgeRag.Infrastructure/Governance/GovernanceService.cs')
s = p.read_text()
s = replace_once(s,
'''    string? ReviewerModel,
    Guid? PromptProfileId,''',
'''    string? ReviewerModel,
    Guid? PlannerDeploymentId,
    Guid? AnswerDeploymentId,
    Guid? ReviewerDeploymentId,
    Guid? RerankerDeploymentId,
    Guid? PromptProfileId,''',
'rag profile update deployment fields')
s = replace_once(s,
'''public sealed record ModelPriceUpdate(string Model, decimal InputCostPerMillionTokens, decimal OutputCostPerMillionTokens, bool IsActive = true);''',
'''public sealed record ModelPriceUpdate(string Model, decimal InputCostPerMillionTokens, decimal OutputCostPerMillionTokens, bool IsActive = true, Guid? DeploymentId = null);''',
'model price update')
s = replace_once(s,
'''    TenantAccessService tenantAccess,
    RagRuntimeProfileService runtimeProfiles)''',
'''    TenantAccessService tenantAccess,
    RagRuntimeProfileService runtimeProfiles,
    TenantAiProviderService tenantAiProviders)''',
'governance constructor')
s = replace_once(s,
'''            ReviewerModel = resolved.ReviewerModel,
            PromptProfileId = resolved.PromptProfileId,''',
'''            ReviewerModel = resolved.ReviewerModel,
            PlannerDeploymentId = resolved.PlannerEndpoint.DeploymentId,
            AnswerDeploymentId = resolved.AnswerEndpoint.DeploymentId,
            ReviewerDeploymentId = resolved.ReviewerEndpoint.DeploymentId,
            RerankerDeploymentId = resolved.RerankerEndpoint?.DeploymentId,
            PromptProfileId = resolved.PromptProfileId,''',
'default profile resolved deployments')
s = replace_once(s,
'''        entity.PlannerModel = request.PlannerModel?.Trim() ?? string.Empty;
        entity.AnswerModel = request.AnswerModel?.Trim() ?? string.Empty;
        entity.ReviewerModel = request.ReviewerModel?.Trim() ?? string.Empty;
        entity.PromptProfileId = request.PromptProfileId;''',
'''        var tenantId = await db.KnowledgeBases.AsNoTracking().Where(x => x.Id == knowledgeBaseId).Select(x => x.TenantId).SingleAsync(ct);
        if (request.PlannerDeploymentId is Guid plannerDeploymentId)
            _ = await tenantAiProviders.ResolveEndpointWithFallbackAsync(tenantId, plannerDeploymentId, AiModelTypes.Chat, ct);
        if (request.AnswerDeploymentId is Guid answerDeploymentId)
            _ = await tenantAiProviders.ResolveEndpointWithFallbackAsync(tenantId, answerDeploymentId, AiModelTypes.Chat, ct);
        if (request.ReviewerDeploymentId is Guid reviewerDeploymentId)
            _ = await tenantAiProviders.ResolveEndpointWithFallbackAsync(tenantId, reviewerDeploymentId, AiModelTypes.Chat, ct);
        if (request.RerankerDeploymentId is Guid rerankerDeploymentId)
            _ = await tenantAiProviders.ResolveEndpointWithFallbackAsync(tenantId, rerankerDeploymentId, AiModelTypes.Rerank, ct);
        entity.PlannerModel = request.PlannerModel?.Trim() ?? string.Empty;
        entity.AnswerModel = request.AnswerModel?.Trim() ?? string.Empty;
        entity.ReviewerModel = request.ReviewerModel?.Trim() ?? string.Empty;
        entity.PlannerDeploymentId = request.PlannerDeploymentId;
        entity.AnswerDeploymentId = request.AnswerDeploymentId;
        entity.ReviewerDeploymentId = request.ReviewerDeploymentId;
        entity.RerankerDeploymentId = request.RerankerDeploymentId;
        entity.PromptProfileId = request.PromptProfileId;''',
'save profile deployment validation')
s = replace_once(s,
'''        entity.UpdatedByUserId = actor.UserId;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<IReadOnlyList<RagRunEntity>>''',
'''        entity.UpdatedByUserId = actor.UserId;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        db.AuditLogs.Add(new AuditLogEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = actor.UserId, ApiKeyId = actor.ApiKeyId,
            Action = "rag.profile.routing.saved", ResourceType = "rag-profile", ResourceId = knowledgeBaseId.ToString(),
            Summary = "Updated RAG role deployment routing.",
            DetailsJson = JsonSerializer.Serialize(new { entity.PlannerDeploymentId, entity.AnswerDeploymentId, entity.ReviewerDeploymentId, entity.RerankerDeploymentId }, WebJson),
            StatusCode = 200, CreatedAt = entity.UpdatedAt
        });
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<IReadOnlyList<RagRunEntity>>''',
'profile routing audit')
s = replace_once(s,
'''        if (string.IsNullOrWhiteSpace(request.Model)) throw new InvalidOperationException("模型名称不能为空。");
        var model = request.Model.Trim();
        var entity = await db.Set<ModelPriceEntity>().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Model == model, ct)
                     ?? new ModelPriceEntity { Id = Guid.NewGuid(), TenantId = tenantId, Model = model };
        if (db.Entry(entity).State == EntityState.Detached) db.Add(entity);''',
'''        string model;
        if (request.DeploymentId is Guid deploymentId)
        {
            var deployment = await db.AiModelDeployments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == deploymentId && x.TenantId == tenantId, ct)
                ?? throw new InvalidOperationException("Pricing Deployment 不存在或不属于当前租户。");
            model = deployment.RemoteModelName;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Model)) throw new InvalidOperationException("模型名称不能为空。");
            model = request.Model.Trim();
        }
        var entity = request.DeploymentId is Guid priceDeploymentId
            ? await db.ModelPrices.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.DeploymentId == priceDeploymentId, ct)
            : await db.ModelPrices.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.DeploymentId == null && x.Model == model, ct);
        entity ??= new ModelPriceEntity { Id = Guid.NewGuid(), TenantId = tenantId };
        if (db.Entry(entity).State == EntityState.Detached) db.Add(entity);
        entity.DeploymentId = request.DeploymentId;
        entity.Model = model;''',
'model price deployment identity')
s = replace_once(s,
'''        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<IReadOnlyList<PromptProfileEntity>>''',
'''        entity.UpdatedAt = DateTimeOffset.UtcNow;
        db.AuditLogs.Add(new AuditLogEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = actor.UserId, ApiKeyId = actor.ApiKeyId,
            Action = "ai.price.saved", ResourceType = "model-price", ResourceId = entity.Id.ToString(),
            Summary = request.DeploymentId.HasValue ? "Updated deployment-level AI price." : "Updated legacy model-name AI price.",
            DetailsJson = JsonSerializer.Serialize(new { entity.DeploymentId, entity.Model, entity.IsActive }, WebJson),
            StatusCode = 200, CreatedAt = entity.UpdatedAt
        });
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<IReadOnlyList<PromptProfileEntity>>''',
'model price audit')
p.write_text(s)

# RAG agents use role-specific resolved endpoints, not one shared endpoint with a raw model name.
p = Path('src/KnowledgeRag.Infrastructure/Conversations/RagAgentService.cs')
s = p.read_text()
s = replace_once(s, 'BuildAgent("QueryPlannerAgent", profile.PlannerModel, instructions)', 'BuildAgent("QueryPlannerAgent", profile.PlannerEndpoint, instructions)', 'planner endpoint')
s = replace_once(s, 'BuildAgent("KnowledgeAnswerAgent", profile.AnswerModel, instructions, tools)', 'BuildAgent("KnowledgeAnswerAgent", profile.AnswerEndpoint, instructions, tools)', 'answer endpoint')
s = s.replace('BuildAgent("ComparisonSpecialistAgent", model,', 'BuildAgent("ComparisonSpecialistAgent", profile.ReviewerEndpoint,')
s = s.replace('BuildAgent("SummarySpecialistAgent", model,', 'BuildAgent("SummarySpecialistAgent", profile.ReviewerEndpoint,')
s = s.replace('BuildAgent("ExtractionSpecialistAgent", model,', 'BuildAgent("ExtractionSpecialistAgent", profile.ReviewerEndpoint,')
s = s.replace('BuildAgent("ResearchSpecialistAgent", model,', 'BuildAgent("ResearchSpecialistAgent", profile.ReviewerEndpoint,')
s = replace_once(s, '        var model = profile.ReviewerModel;\n', '', 'remove specialist raw model')
s = replace_once(s, 'BuildAgent("EvidenceReviewerAgent", profile.ReviewerModel, instructions)', 'BuildAgent("EvidenceReviewerAgent", profile.ReviewerEndpoint, instructions)', 'reviewer endpoint')
s = replace_once(s,
'''    private AIAgent BuildAgent(string name, string model, string instructions, IList<AITool>? tools = null)
    {
        var chatClient = aiClientFactory.CreateChatClient(Profile.ChatEndpoint, model);''',
'''    private AIAgent BuildAgent(string name, ResolvedAiEndpoint endpoint, string instructions, IList<AITool>? tools = null)
    {
        var chatClient = aiClientFactory.CreateChatClient(endpoint, endpoint.Model);''',
'build agent endpoint signature')
p.write_text(s)

# Tenant health endpoint.
p = Path('src/KnowledgeRag.Api/Endpoints/TenantEndpoints.cs')
s = p.read_text()
s = replace_once(s,
'''        group.MapDelete("/{tenantId:guid}/ai/deployments/{deploymentId:guid}", async (Guid tenantId, Guid deploymentId, ClaimsPrincipal principal, TenantAiProviderService service, CancellationToken ct) =>
        {
            await service.DeleteDeploymentAsync(HttpActorContext.From(principal), tenantId, deploymentId, ct);
            return Results.NoContent();
        });
''',
'''        group.MapDelete("/{tenantId:guid}/ai/deployments/{deploymentId:guid}", async (Guid tenantId, Guid deploymentId, ClaimsPrincipal principal, TenantAiProviderService service, CancellationToken ct) =>
        {
            await service.DeleteDeploymentAsync(HttpActorContext.From(principal), tenantId, deploymentId, ct);
            return Results.NoContent();
        });

        group.MapPost("/{tenantId:guid}/ai/deployments/{deploymentId:guid}/health", async (Guid tenantId, Guid deploymentId, ClaimsPrincipal principal, TenantAiProviderService service, CancellationToken ct) =>
            Results.Ok(await service.TestDeploymentHealthAsync(HttpActorContext.From(principal), tenantId, deploymentId, ct)));
''',
'tenant deployment health endpoint')
p.write_text(s)

# Changelog entries in the single current Unreleased section.
p = Path('CHANGELOG.md')
s = p.read_text()
s = s.replace('### Added\n\n', '### Added\n\n- 新增 **Deployment-level AI Routing Governance**：KB RAG Profile 可将 Planner / Answer / Reviewer / Reranker 显式绑定到租户 Model Deployment；Deployment 支持同类型显式 fallback、持久化健康状态与安全 health check，运行开始时一次性解析实际 endpoint snapshot。\n- 新增 deployment-first AI pricing / usage identity：Model Price 可绑定 Deployment，Budget Reservation 与 usage ledger 记录实际 resolved DeploymentId / ProviderType；同名模型跨 Provider 可独立计价，旧 model-name price 继续作为兼容 fallback。\n', 1)
s = s.replace('### Changed\n\n', '### Changed\n\n- RAG Agent 不再用一个共享 ChatEndpoint 搭配 raw model string 解释所有角色；Planner / Answer / Reviewer / Specialist 现在使用各自已解析 Deployment endpoint，显式绑定不可用时只沿配置好的 fallback 链选择，不在运行中动态漂移到其他 Provider。\n', 1)
p.write_text(s)
