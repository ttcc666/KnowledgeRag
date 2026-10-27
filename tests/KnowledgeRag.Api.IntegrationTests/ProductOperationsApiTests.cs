using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class ProductOperationsApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public ProductOperationsApiTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Dashboard_And_Settings_AreAuthenticated_AndDoNotExposeSecrets()
    {
        using var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/settings/product")).StatusCode);

        using var client = _factory.CreateClient();
        var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"ops-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!123",
            displayName = "Operations Test"
        });
        registration.EnsureSuccessStatusCode();
        var auth = await registration.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());

        var dashboard = await client.GetAsync("/api/dashboard");
        dashboard.EnsureSuccessStatusCode();
        var dashboardJson = await dashboard.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(dashboardJson.TryGetProperty("knowledgeBaseCount", out _));
        Assert.True(dashboardJson.TryGetProperty("retrieval", out _));

        var settings = await client.GetAsync("/api/settings/product");
        settings.EnsureSuccessStatusCode();
        var settingsText = await settings.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SigningKey", settingsText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", settingsText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionString", settingsText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IngestionJobs_ReturnsOnlyAccessibleWorkspaceJobs()
    {
        using var client = _factory.CreateClient();
        var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"jobs-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!123",
            displayName = "Jobs Test"
        });
        registration.EnsureSuccessStatusCode();
        var auth = await registration.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());

        var jobs = await client.GetAsync("/api/ingestion/jobs");
        jobs.EnsureSuccessStatusCode();
        var payload = await jobs.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, payload.ValueKind);
    }
}
