using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class AuthTenantApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public AuthTenantApiTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_Refresh_RotatesRefreshToken()
    {
        using var client = _factory.CreateClient();
        var auth = await RegisterAsync(client, $"refresh-{Guid.NewGuid():N}@example.com");
        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = auth.RefreshToken });
        response.EnsureSuccessStatusCode();
        var rotated = await ReadAuthAsync(response);
        Assert.NotEqual(auth.RefreshToken, rotated.RefreshToken);
        Assert.False(string.IsNullOrWhiteSpace(rotated.AccessToken));

        var replay = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = auth.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        var rotatedAfterReplay = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = rotated.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, rotatedAfterReplay.StatusCode);
    }

    [Fact]
    public async Task TenantApiKey_CanAccessOnlyScopedKnowledgeBase()
    {
        using var client = _factory.CreateClient();
        var auth = await RegisterAsync(client, $"apikey-{Guid.NewGuid():N}@example.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var tenants = await client.GetFromJsonAsync<JsonElement>("/api/tenants");
        var tenantId = tenants[0].GetProperty("id").GetGuid();
        var kbResponse = await client.PostAsJsonAsync("/api/knowledge-bases", new { name = "Scoped KB", tenantId });
        kbResponse.EnsureSuccessStatusCode();
        var kbId = (await kbResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var otherResponse = await client.PostAsJsonAsync("/api/knowledge-bases", new { name = "Other KB", tenantId });
        otherResponse.EnsureSuccessStatusCode();
        var otherKbId = (await otherResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var keyResponse = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/api-keys", new
        {
            name = "integration", scopes = new[] { "knowledge:read" }, knowledgeBaseId = kbId
        });
        keyResponse.EnsureSuccessStatusCode();
        var rawKey = (await keyResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("apiKey").GetString();

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-Api-Key", rawKey);
        var getKb = await client.GetAsync($"/api/knowledge-bases/{kbId}");
        Assert.Equal(HttpStatusCode.OK, getKb.StatusCode);
        var list = await client.GetFromJsonAsync<JsonElement>("/api/knowledge-bases");
        Assert.Single(list.EnumerateArray());
        var forbidden = await client.GetAsync($"/api/knowledge-bases/{otherKbId}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task KnowledgeAdminApiKey_DoesNotImplicitlyGetTenantAdminScope()
    {
        using var client = _factory.CreateClient();
        var auth = await RegisterAsync(client, $"scope-{Guid.NewGuid():N}@example.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var tenants = await client.GetFromJsonAsync<JsonElement>("/api/tenants");
        var tenantId = tenants[0].GetProperty("id").GetGuid();

        var knowledgeKeyResponse = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/api-keys", new
        {
            name = "knowledge-admin", scopes = new[] { "knowledge:admin" }
        });
        knowledgeKeyResponse.EnsureSuccessStatusCode();
        var knowledgeKey = (await knowledgeKeyResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("apiKey").GetString();

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-Api-Key", knowledgeKey);
        var denied = await client.GetAsync($"/api/tenants/{tenantId}/members");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        client.DefaultRequestHeaders.Remove("X-Api-Key");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var tenantKeyResponse = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/api-keys", new
        {
            name = "tenant-admin", scopes = new[] { "tenant:admin" }
        });
        tenantKeyResponse.EnsureSuccessStatusCode();
        var tenantKey = (await tenantKeyResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("apiKey").GetString();

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-Api-Key", tenantKey);
        var allowed = await client.GetAsync($"/api/tenants/{tenantId}/members");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task User_CannotReadAnotherUsersKnowledgeBase()
    {
        using var owner = _factory.CreateClient();
        var ownerAuth = await RegisterAsync(owner, $"owner-{Guid.NewGuid():N}@example.com");
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerAuth.AccessToken);
        var kbResponse = await owner.PostAsJsonAsync("/api/knowledge-bases", new { name = "Private KB" });
        kbResponse.EnsureSuccessStatusCode();
        var kbId = (await kbResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var stranger = _factory.CreateClient();
        var strangerAuth = await RegisterAsync(stranger, $"stranger-{Guid.NewGuid():N}@example.com");
        stranger.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", strangerAuth.AccessToken);
        var response = await stranger.GetAsync($"/api/knowledge-bases/{kbId}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<TestAuth> RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Passw0rd!123", displayName = "Test User" });
        response.EnsureSuccessStatusCode();
        return await ReadAuthAsync(response);
    }

    private static async Task<TestAuth> ReadAuthAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new TestAuth(json.GetProperty("accessToken").GetString()!, json.GetProperty("refreshToken").GetString()!);
    }

    private sealed record TestAuth(string AccessToken, string RefreshToken);
}
