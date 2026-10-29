using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Governance;
using KnowledgeRag.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class GovernanceApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public GovernanceApiTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Governance_Profile_Prompt_Webhook_Evaluation_And_Audit_ArePersisted()
    {
        using var client = _factory.CreateClient();
        var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"governance-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!123",
            displayName = "Governance Test"
        });
        registration.EnsureSuccessStatusCode();
        var auth = await registration.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());

        var tenants = await client.GetFromJsonAsync<JsonElement>("/api/tenants");
        Assert.Equal(JsonValueKind.Array, tenants.ValueKind);
        Assert.True(tenants.GetArrayLength() > 0);
        var tenantId = tenants[0].GetProperty("id").GetGuid();

        var createKb = await client.PostAsJsonAsync("/api/knowledge-bases", new { tenantId, name = "Governance KB", description = "governance tests" });
        createKb.EnsureSuccessStatusCode();
        var kb = await createKb.Content.ReadFromJsonAsync<JsonElement>();
        var knowledgeBaseId = kb.GetProperty("id").GetGuid();

        var saveProfile = await client.PutAsJsonAsync($"/api/governance/knowledge-bases/{knowledgeBaseId}/rag-profile", new
        {
            enablePlanner = true,
            plannerMaxQueries = 4,
            multiQueryTopK = 8,
            maxMergedEvidence = 12,
            enableReviewer = true,
            reviewerMaxRetries = 2,
            reviewerMinConfidence = .8,
            plannerModel = "planner-test",
            answerModel = "answer-test",
            reviewerModel = "reviewer-test",
            promptProfileId = (Guid?)null,
            agentMode = "specialists",
            monthlyBudgetUsd = 15.5m,
            dailyRunLimit = 100
        });
        saveProfile.EnsureSuccessStatusCode();
        var profile = await client.GetFromJsonAsync<JsonElement>($"/api/governance/knowledge-bases/{knowledgeBaseId}/rag-profile");
        Assert.Equal(4, profile.GetProperty("plannerMaxQueries").GetInt32());
        Assert.Equal("answer-test", profile.GetProperty("answerModel").GetString());
        Assert.Equal("specialists", profile.GetProperty("agentMode").GetString());

        var savePrice = await client.PutAsJsonAsync($"/api/governance/model-prices?tenantId={tenantId}", new
        {
            model = "answer-test",
            inputCostPerMillionTokens = 2.5m,
            outputCostPerMillionTokens = 10m,
            isActive = true
        });
        savePrice.EnsureSuccessStatusCode();
        var prices = await client.GetFromJsonAsync<JsonElement>($"/api/governance/model-prices?tenantId={tenantId}");
        Assert.Contains(prices.EnumerateArray(), x => x.GetProperty("model").GetString() == "answer-test");

        // Regression: SQLite cannot translate ORDER BY DateTimeOffset. Cost estimation must
        // filter prices in SQL and select the newest price in memory.
        using (var scope = _factory.Services.CreateScope())
        {
            var runtimeProfiles = scope.ServiceProvider.GetRequiredService<RagRuntimeProfileService>();
            var estimatedCost = await runtimeProfiles.EstimateCostAsync(tenantId, "answer-test", 1_000_000, 1_000_000);
            Assert.Equal(12.5m, estimatedCost);
        }

        // Deployment-first routing/pricing: one unhealthy primary deterministically resolves to its explicit fallback.
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

        var savePrompt = await client.PostAsJsonAsync($"/api/governance/prompts?tenantId={tenantId}", new
        {
            id = (Guid?)null,
            knowledgeBaseId,
            name = "Production",
            plannerPrompt = "Preserve identifiers.",
            answerPrompt = "Lead with conclusion.",
            reviewerPrompt = "Dates require evidence.",
            isActive = true
        });
        savePrompt.EnsureSuccessStatusCode();
        var prompts = await client.GetFromJsonAsync<JsonElement>($"/api/governance/prompts?tenantId={tenantId}");
        Assert.Contains(prompts.EnumerateArray(), x => x.GetProperty("name").GetString() == "Production");

        var secret = "super-secret-webhook-value";
        var saveWebhook = await client.PostAsJsonAsync($"/api/governance/webhooks?tenantId={tenantId}", new
        {
            id = (Guid?)null,
            name = "Ops",
            url = "https://example.com/knowledge-rag-hook",
            eventTypes = new[] { "rag.run.completed", "evaluation.completed" },
            secret,
            isActive = true
        });
        saveWebhook.EnsureSuccessStatusCode();
        var webhookResponseText = await saveWebhook.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secret, webhookResponseText, StringComparison.Ordinal);
        var webhooksText = await client.GetStringAsync($"/api/governance/webhooks?tenantId={tenantId}");
        Assert.DoesNotContain(secret, webhooksText, StringComparison.Ordinal);
        Assert.Contains("hasSecret", webhooksText, StringComparison.Ordinal);

        var createDataset = await client.PostAsJsonAsync($"/api/governance/evaluation/datasets?tenantId={tenantId}", new
        {
            knowledgeBaseId,
            name = "Regression",
            description = "Critical production cases"
        });
        createDataset.EnsureSuccessStatusCode();
        var dataset = await createDataset.Content.ReadFromJsonAsync<JsonElement>();
        var datasetId = dataset.GetProperty("id").GetGuid();
        var addCase = await client.PostAsJsonAsync($"/api/governance/evaluation/datasets/{datasetId}/cases?tenantId={tenantId}", new
        {
            category = "fact",
            query = "What is the policy?",
            expectedAnswerContains = "policy",
            requiresKnowledge = true
        });
        addCase.EnsureSuccessStatusCode();
        var cases = await client.GetFromJsonAsync<JsonElement>($"/api/governance/evaluation/datasets/{datasetId}/cases?tenantId={tenantId}");
        Assert.Single(cases.EnumerateArray());

        var audit = await client.GetFromJsonAsync<JsonElement>($"/api/governance/audit?tenantId={tenantId}");
        Assert.True(audit.GetArrayLength() >= 7);
        Assert.Contains(audit.EnumerateArray(), x => x.GetProperty("resourceType").GetString() == "webhook");
        Assert.Contains(audit.EnumerateArray(), x => x.GetProperty("action").GetString() == "rag.profile.routing.saved");
        Assert.Contains(audit.EnumerateArray(), x => x.GetProperty("action").GetString() == "ai.price.saved");
    }
}
