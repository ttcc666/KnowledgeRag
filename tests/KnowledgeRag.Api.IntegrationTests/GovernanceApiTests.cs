using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
        Assert.True(audit.GetArrayLength() >= 4);
        Assert.Contains(audit.EnumerateArray(), x => x.GetProperty("resourceType").GetString() == "webhook");
    }
}
