using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class KnowledgeBaseIndexGenerationApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public KnowledgeBaseIndexGenerationApiTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Owner_CanActivateEmptyIndexGeneration_AndReadSnapshotWithoutSecrets()
    {
        using var client = _factory.CreateClient();
        var auth = await RegisterAsync(client, $"index-generation-{Guid.NewGuid():N}@example.com");
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
        var current = JsonDocument.Parse(currentBody).RootElement;
        Assert.Equal(generationId, current.GetProperty("activeIndexGenerationId").GetGuid());
        Assert.Equal("active", current.GetProperty("latestStatus").GetString());
    }

    [Fact]
    public async Task Viewer_CanReadIndexStatus_ButCannotStartRebuild()
    {
        using var owner = _factory.CreateClient();
        var ownerAuth = await RegisterAsync(owner, $"index-owner-{Guid.NewGuid():N}@example.com");
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerAuth.AccessToken);
        var tenantId = await FirstTenantIdAsync(owner);
        var create = await owner.PostAsJsonAsync("/api/knowledge-bases", new { name = "Viewer Index Test", tenantId });
        create.EnsureSuccessStatusCode();
        var knowledgeBaseId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var viewer = _factory.CreateClient();
        var viewerEmail = $"index-viewer-{Guid.NewGuid():N}@example.com";
        var viewerAuth = await RegisterAsync(viewer, viewerEmail);
        viewer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerAuth.AccessToken);
        var addMember = await owner.PutAsJsonAsync($"/api/knowledge-bases/{knowledgeBaseId}/members", new { email = viewerEmail, role = "Viewer" });
        addMember.EnsureSuccessStatusCode();
        viewer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewerAuth.AccessToken);

        var read = await viewer.GetAsync($"/api/knowledge-bases/{knowledgeBaseId}/embedding-index");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var rebuild = await viewer.PostAsync($"/api/knowledge-bases/{knowledgeBaseId}/embedding-index/reindex", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, rebuild.StatusCode);
    }

    private static async Task<Guid> FirstTenantIdAsync(HttpClient client)
    {
        var tenants = await client.GetFromJsonAsync<JsonElement>("/api/tenants");
        return tenants[0].GetProperty("id").GetGuid();
    }

    private static async Task<TestAuth> RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "Passw0rd!123",
            displayName = "Test User"
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new TestAuth(json.GetProperty("accessToken").GetString()!);
    }

    private sealed record TestAuth(string AccessToken);
}
