using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class TenantAiProviderApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public TenantAiProviderApiTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Owner_CanActivateEmptyIndexGeneration_AndReadSnapshotWithoutSecrets()
    {
        using var client = _factory.CreateClient();
        var auth = await RegisterAsync(client, $"ai-index-generation-{Guid.NewGuid():N}@example.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var tenantId = await FirstTenantIdAsync(client);
        var create = await client.PostAsJsonAsync("/api/knowledge-bases", new { name = "Generation Test", tenantId });
        create.EnsureSuccessStatusCode();
        var knowledgeBaseId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var initial = await client.GetFromJsonAsync<JsonElement>($"/api/knowledge-bases/{knowledgeBaseId}/embedding-index");
        Assert.Equal(Guid.Empty, initial.GetProperty("activeIndexGenerationId").GetGuid());

        using var start = await client.PostAsync($"/api/knowledge-bases/{knowledgeBaseId}/embedding-index/reindex", content: null);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var started = await start.Content.ReadFromJsonAsync<JsonElement>();
        var generationId = started.GetProperty("activeIndexGenerationId").GetGuid();

        Assert.NotEqual(Guid.Empty, generationId);
        Assert.Equal("active", started.GetProperty("latestStatus").GetString());
        Assert.Equal(0, started.GetProperty("expectedDocumentCount").GetInt32());
        Assert.False(started.GetProperty("activeIsTenantManaged").GetBoolean());
        Assert.Equal("Platform default", started.GetProperty("activeDeploymentDisplayName").GetString());

        var currentBody = await client.GetStringAsync($"/api/knowledge-bases/{knowledgeBaseId}/embedding-index");
        Assert.DoesNotContain("ApiKey", currentBody, StringComparison.OrdinalIgnoreCase);
        using var currentDocument = JsonDocument.Parse(currentBody);
        Assert.Equal(generationId, currentDocument.RootElement.GetProperty("activeIndexGenerationId").GetGuid());
        Assert.Equal("active", currentDocument.RootElement.GetProperty("latestStatus").GetString());
    }

    [Fact]
    public async Task Owner_CanConfigureProviderDeploymentAndDefaults_WithoutSecretDisclosure()
    {
        using var client = _factory.CreateClient();
        var auth = await RegisterAsync(client, $"ai-owner-{Guid.NewGuid():N}@example.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var tenantId = await FirstTenantIdAsync(client);

        const string secret = "tenant-secret-must-never-be-returned";
        var connectionResponse = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/ai/connections", new
        {
            name = "OpenAI Production",
            providerType = "openai-compatible",
            baseUrl = "https://api.openai.com/v1",
            credential = secret,
            isEnabled = true
        });
        connectionResponse.EnsureSuccessStatusCode();
        var connectionBody = await connectionResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secret, connectionBody, StringComparison.Ordinal);
        var connection = JsonDocument.Parse(connectionBody).RootElement;
        Assert.True(connection.GetProperty("hasCredential").GetBoolean());
        var connectionId = connection.GetProperty("id").GetGuid();

        var listBody = await client.GetStringAsync($"/api/tenants/{tenantId}/ai/connections");
        Assert.DoesNotContain(secret, listBody, StringComparison.Ordinal);

        var chatId = await CreateDeploymentAsync(client, tenantId, connectionId, "Chat Production", "gpt-4.1", "chat");
        var rerankerId = await CreateDeploymentAsync(client, tenantId, connectionId, "Reranker", "gpt-4o-mini", "rerank");
        var embeddingId = await CreateDeploymentAsync(client, tenantId, connectionId, "Embedding Target", "text-embedding-3-small", "embedding", 1536);

        var defaultsResponse = await client.PutAsJsonAsync($"/api/tenants/{tenantId}/ai/defaults", new
        {
            defaultChatDeploymentId = chatId,
            defaultEmbeddingDeploymentId = embeddingId,
            defaultRerankerDeploymentId = rerankerId
        });
        defaultsResponse.EnsureSuccessStatusCode();
        var defaults = await defaultsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(chatId, defaults.GetProperty("defaultChatDeploymentId").GetGuid());
        Assert.Equal(embeddingId, defaults.GetProperty("defaultEmbeddingDeploymentId").GetGuid());
        Assert.Equal(rerankerId, defaults.GetProperty("defaultRerankerDeploymentId").GetGuid());
    }

    [Fact]
    public async Task Defaults_RejectDeploymentWithWrongModelType()
    {
        using var client = _factory.CreateClient();
        var auth = await RegisterAsync(client, $"ai-type-{Guid.NewGuid():N}@example.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var tenantId = await FirstTenantIdAsync(client);
        var connectionId = await CreateConnectionAsync(client, tenantId, "Type Test");
        var chatId = await CreateDeploymentAsync(client, tenantId, connectionId, "Chat", "gpt-4o-mini", "chat");

        var response = await client.PutAsJsonAsync($"/api/tenants/{tenantId}/ai/defaults", new
        {
            defaultChatDeploymentId = (Guid?)null,
            defaultEmbeddingDeploymentId = (Guid?)null,
            defaultRerankerDeploymentId = chatId
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Member_CanReadButCannotMutateTenantAiConfiguration()
    {
        using var owner = _factory.CreateClient();
        var ownerAuth = await RegisterAsync(owner, $"ai-owner-member-{Guid.NewGuid():N}@example.com");
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerAuth.AccessToken);
        var tenantId = await FirstTenantIdAsync(owner);

        using var member = _factory.CreateClient();
        var memberEmail = $"ai-member-{Guid.NewGuid():N}@example.com";
        var memberAuth = await RegisterAsync(member, memberEmail);
        member.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberAuth.AccessToken);

        var addMember = await owner.PutAsJsonAsync($"/api/tenants/{tenantId}/members", new { email = memberEmail, role = "Member" });
        addMember.EnsureSuccessStatusCode();

        var readable = await member.GetAsync($"/api/tenants/{tenantId}/ai/connections");
        Assert.Equal(HttpStatusCode.OK, readable.StatusCode);
        var denied = await member.PostAsJsonAsync($"/api/tenants/{tenantId}/ai/connections", new
        {
            name = "Denied",
            providerType = "openai-compatible",
            baseUrl = "https://api.openai.com/v1",
            credential = "not-written",
            isEnabled = true
        });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Theory]
    [InlineData("http://api.openai.com/v1")]
    [InlineData("https://127.0.0.1/v1")]
    [InlineData("https://localhost/v1")]
    [InlineData("https://10.0.0.1/v1")]
    public async Task TenantManagedProvider_RejectsUnsafeBaseUrls(string baseUrl)
    {
        using var client = _factory.CreateClient();
        var auth = await RegisterAsync(client, $"ai-ssrf-{Guid.NewGuid():N}@example.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var tenantId = await FirstTenantIdAsync(client);

        var response = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/ai/connections", new
        {
            name = "Unsafe",
            providerType = "openai-compatible",
            baseUrl,
            credential = "secret",
            isEnabled = true
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<Guid> CreateConnectionAsync(HttpClient client, Guid tenantId, string name)
    {
        var response = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/ai/connections", new
        {
            name,
            providerType = "openai-compatible",
            baseUrl = "https://api.openai.com/v1",
            credential = "test-key",
            isEnabled = true
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateDeploymentAsync(HttpClient client, Guid tenantId, Guid connectionId, string displayName, string remoteModelName, string modelType, int? embeddingDimensions = null)
    {
        var response = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/ai/deployments", new
        {
            connectionId,
            displayName,
            remoteModelName,
            modelType,
            embeddingDimensions,
            isEnabled = true
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> FirstTenantIdAsync(HttpClient client)
    {
        var tenants = await client.GetFromJsonAsync<JsonElement>("/api/tenants");
        return tenants[0].GetProperty("id").GetGuid();
    }

    private static async Task<TestAuth> RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Passw0rd!123", displayName = "Test User" });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new TestAuth(json.GetProperty("accessToken").GetString()!);
    }

    private sealed record TestAuth(string AccessToken);
}
