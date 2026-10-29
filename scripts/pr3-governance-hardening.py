from pathlib import Path
import re


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected one match, found {count}")
    return text.replace(old, new, 1)

# Preserve immutable deployment identity for historical run/price rows.
p = Path('src/KnowledgeRag.Infrastructure/Persistence/AppDbContext.cs')
s = p.read_text()
s = replace_once(s,
'''            entity.HasOne<ConversationEntity>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<AiModelDeploymentEntity>().WithMany().HasForeignKey(x => x.AnswerDeploymentId).OnDelete(DeleteBehavior.SetNull);''',
'''            entity.HasOne<ConversationEntity>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<AiModelDeploymentEntity>().WithMany().HasForeignKey(x => x.AnswerDeploymentId).OnDelete(DeleteBehavior.Restrict);''',
'rag run deployment delete behavior')
s = replace_once(s,
'''            entity.HasOne<TenantEntity>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AiModelDeploymentEntity>().WithMany().HasForeignKey(x => x.DeploymentId).OnDelete(DeleteBehavior.SetNull);''',
'''            entity.HasOne<TenantEntity>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AiModelDeploymentEntity>().WithMany().HasForeignKey(x => x.DeploymentId).OnDelete(DeleteBehavior.Restrict);''',
'model price deployment delete behavior')
p.write_text(s)

# Preserve health on display/fallback-only edits; block deletion once identity is used by price/run history.
p = Path('src/KnowledgeRag.Infrastructure/Tenancy/TenantAiProviderService.cs')
s = p.read_text()
s = replace_once(s,
'''            entity = await db.Set<AiModelDeploymentEntity>().SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct)
                ?? throw new KeyNotFoundException("Model Deployment 不存在。");
            entity.ConnectionId = request.ConnectionId;''',
'''            entity = await db.Set<AiModelDeploymentEntity>().SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct)
                ?? throw new KeyNotFoundException("Model Deployment 不存在。");
            var healthTargetChanged = entity.ConnectionId != request.ConnectionId ||
                                      !entity.RemoteModelName.Equals(remoteModel, StringComparison.Ordinal) ||
                                      !entity.ModelType.Equals(modelType, StringComparison.OrdinalIgnoreCase) ||
                                      entity.IsEnabled != request.IsEnabled;
            entity.ConnectionId = request.ConnectionId;''',
'deployment health target change')
s = replace_once(s,
'''            entity.FallbackDeploymentId = request.FallbackDeploymentId;
            entity.HealthStatus = AiDeploymentHealthStatuses.Unknown;
            entity.LastHealthCheckedAt = null;
            entity.LastHealthError = null;
            entity.IsEnabled = request.IsEnabled;''',
'''            entity.FallbackDeploymentId = request.FallbackDeploymentId;
            if (healthTargetChanged)
            {
                entity.HealthStatus = AiDeploymentHealthStatuses.Unknown;
                entity.LastHealthCheckedAt = null;
                entity.LastHealthError = null;
            }
            entity.IsEnabled = request.IsEnabled;''',
'conditional health reset')
s = replace_once(s,
'''        if (await db.KnowledgeBaseRagProfiles.AnyAsync(x => x.PlannerDeploymentId == deploymentId || x.AnswerDeploymentId == deploymentId ||
            x.ReviewerDeploymentId == deploymentId || x.RerankerDeploymentId == deploymentId, ct))
            throw new InvalidOperationException("该 Deployment 正在被知识库 RAG Profile 使用，请先修改角色路由。");
        db.Remove(entity);''',
'''        if (await db.KnowledgeBaseRagProfiles.AnyAsync(x => x.PlannerDeploymentId == deploymentId || x.AnswerDeploymentId == deploymentId ||
            x.ReviewerDeploymentId == deploymentId || x.RerankerDeploymentId == deploymentId, ct))
            throw new InvalidOperationException("该 Deployment 正在被知识库 RAG Profile 使用，请先修改角色路由。");
        if (await db.ModelPrices.AnyAsync(x => x.DeploymentId == deploymentId, ct))
            throw new InvalidOperationException("该 Deployment 已绑定价格记录；为保持成本身份不可变，请停用而不是删除。");
        if (await db.RagRuns.AnyAsync(x => x.AnswerDeploymentId == deploymentId, ct))
            throw new InvalidOperationException("该 Deployment 已出现在历史 RAG Run；为保持运行账本身份不可变，请停用而不是删除。");
        db.Remove(entity);''',
'deployment immutable identity delete guards')
p.write_text(s)

# Extend the existing shared ApiFactory governance test (avoid a second fixture class racing environment variables).
p = Path('tests/KnowledgeRag.Api.IntegrationTests/GovernanceApiTests.cs')
s = p.read_text()
s = replace_once(s,
'''using System.Text.Json;
using KnowledgeRag.Infrastructure.Governance;''',
'''using System.Text.Json;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Governance;
using KnowledgeRag.Infrastructure.Persistence;''',
'governance test usings')
needle = '''        using (var scope = _factory.Services.CreateScope())
        {
            var runtimeProfiles = scope.ServiceProvider.GetRequiredService<RagRuntimeProfileService>();
            var estimatedCost = await runtimeProfiles.EstimateCostAsync(tenantId, "answer-test", 1_000_000, 1_000_000);
            Assert.Equal(12.5m, estimatedCost);
        }

'''
insert = needle + '''        // Deployment-first routing/pricing: one unhealthy primary deterministically resolves to its explicit fallback.
        var connectionResponse = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/ai/connections", new
        {
            name = "Governance Provider",
            providerType = "openai-compatible",
            baseUrl = "https://api.openai.com/v1",
            credential = "governance-test-secret",
            isEnabled = true
        });
        connectionResponse.EnsureSuccessStatusCode();
        var connectionId = (await connectionResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var fallbackResponse = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/ai/deployments", new
        {
            connectionId,
            displayName = "Fallback Chat",
            remoteModelName = "gpt-fallback",
            modelType = "chat",
            embeddingDimensions = (int?)null,
            isEnabled = true,
            fallbackDeploymentId = (Guid?)null
        });
        fallbackResponse.EnsureSuccessStatusCode();
        var fallbackDeploymentId = (await fallbackResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var primaryResponse = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/ai/deployments", new
        {
            connectionId,
            displayName = "Primary Chat",
            remoteModelName = "gpt-primary",
            modelType = "chat",
            embeddingDimensions = (int?)null,
            isEnabled = true,
            fallbackDeploymentId
        });
        primaryResponse.EnsureSuccessStatusCode();
        var primaryDeploymentId = (await primaryResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var selfFallback = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/ai/deployments", new
        {
            id = primaryDeploymentId,
            connectionId,
            displayName = "Primary Chat",
            remoteModelName = "gpt-primary",
            modelType = "chat",
            embeddingDimensions = (int?)null,
            isEnabled = true,
            fallbackDeploymentId = primaryDeploymentId
        });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, selfFallback.StatusCode);

        var saveDeploymentProfile = await client.PutAsJsonAsync($"/api/governance/knowledge-bases/{knowledgeBaseId}/rag-profile", new
        {
            enablePlanner = true,
            plannerMaxQueries = 4,
            multiQueryTopK = 8,
            maxMergedEvidence = 12,
            enableReviewer = true,
            reviewerMaxRetries = 2,
            reviewerMinConfidence = .8,
            plannerModel = "",
            answerModel = "",
            reviewerModel = "",
            plannerDeploymentId = primaryDeploymentId,
            answerDeploymentId = primaryDeploymentId,
            reviewerDeploymentId = fallbackDeploymentId,
            rerankerDeploymentId = (Guid?)null,
            promptProfileId = (Guid?)null,
            agentMode = "specialists",
            monthlyBudgetUsd = 15.5m,
            dailyRunLimit = 100
        });
        saveDeploymentProfile.EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var primary = await db.AiModelDeployments.SingleAsync(x => x.Id == primaryDeploymentId);
            primary.HealthStatus = AiDeploymentHealthStatuses.Unhealthy;
            primary.LastHealthCheckedAt = DateTimeOffset.UtcNow;
            primary.LastHealthError = "synthetic regression health failure";
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var runtimeProfiles = scope.ServiceProvider.GetRequiredService<RagRuntimeProfileService>();
            var routed = await runtimeProfiles.ResolveAsync(knowledgeBaseId);
            Assert.Equal(fallbackDeploymentId, routed.PlannerEndpoint.DeploymentId);
            Assert.Equal(fallbackDeploymentId, routed.AnswerEndpoint.DeploymentId);
            Assert.Equal("gpt-fallback", routed.AnswerModel);
            Assert.Equal(fallbackDeploymentId, routed.ReviewerEndpoint.DeploymentId);
        }

        var deploymentPrice = await client.PutAsJsonAsync($"/api/governance/model-prices?tenantId={tenantId}", new
        {
            model = "",
            deploymentId = fallbackDeploymentId,
            inputCostPerMillionTokens = 7m,
            outputCostPerMillionTokens = 13m,
            isActive = true
        });
        deploymentPrice.EnsureSuccessStatusCode();
        var deploymentPriceJson = await deploymentPrice.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(fallbackDeploymentId, deploymentPriceJson.GetProperty("deploymentId").GetGuid());
        Assert.Equal("gpt-fallback", deploymentPriceJson.GetProperty("model").GetString());

        using (var scope = _factory.Services.CreateScope())
        {
            var runtimeProfiles = scope.ServiceProvider.GetRequiredService<RagRuntimeProfileService>();
            var deploymentCost = await runtimeProfiles.EstimateCostAsync(tenantId, "gpt-fallback", fallbackDeploymentId, 1_000_000, 1_000_000);
            Assert.Equal(20m, deploymentCost);
        }

'''
s = replace_once(s, needle, insert, 'governance deployment routing tests')
s = replace_once(s,
'''        Assert.True(audit.GetArrayLength() >= 4);
        Assert.Contains(audit.EnumerateArray(), x => x.GetProperty("resourceType").GetString() == "webhook");''',
'''        Assert.True(audit.GetArrayLength() >= 7);
        Assert.Contains(audit.EnumerateArray(), x => x.GetProperty("resourceType").GetString() == "webhook");
        Assert.Contains(audit.EnumerateArray(), x => x.GetProperty("action").GetString() == "rag.profile.routing.saved");
        Assert.Contains(audit.EnumerateArray(), x => x.GetProperty("action").GetString() == "ai.price.saved");''',
'governance routing audit assertions')
p.write_text(s)
