using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class ConversationMembershipRevocationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task ConversationList_HidesMetadataAfterKnowledgeBaseMembershipIsRevoked()
    {
        using var owner = factory.CreateClient();
        var ownerAuth = await RegisterAsync(owner, $"kb-owner-{Guid.NewGuid():N}@example.com");
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerAuth.AccessToken);

        using var knowledgeBaseResponse = await owner.PostAsJsonAsync("/api/knowledge-bases", new { name = "Membership revocation KB" });
        knowledgeBaseResponse.EnsureSuccessStatusCode();
        using var knowledgeBaseJson = JsonDocument.Parse(await knowledgeBaseResponse.Content.ReadAsStringAsync());
        var knowledgeBaseId = knowledgeBaseJson.RootElement.GetProperty("id").GetGuid();

        using var member = factory.CreateClient();
        var memberEmail = $"kb-member-{Guid.NewGuid():N}@example.com";
        var memberAuth = await RegisterAsync(member, memberEmail);
        member.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberAuth.AccessToken);

        using var addMemberResponse = await owner.PutAsJsonAsync(
            $"/api/knowledge-bases/{knowledgeBaseId}/members",
            new { email = memberEmail, role = "Viewer" });
        addMemberResponse.EnsureSuccessStatusCode();

        using var conversationResponse = await member.PostAsJsonAsync(
            "/api/conversations",
            new { knowledgeBaseId, title = "Must disappear after membership revocation" });
        Assert.True(conversationResponse.IsSuccessStatusCode, await conversationResponse.Content.ReadAsStringAsync());
        using var conversationJson = JsonDocument.Parse(await conversationResponse.Content.ReadAsStringAsync());
        var conversationId = conversationJson.RootElement.GetProperty("id").GetGuid();

        var before = await member.GetFromJsonAsync<JsonElement>("/api/conversations");
        Assert.Contains(before.EnumerateArray(), item => item.GetProperty("id").GetGuid() == conversationId);

        var beforeScoped = await member.GetFromJsonAsync<JsonElement>(
            $"/api/conversations?knowledgeBaseId={knowledgeBaseId}");
        Assert.Contains(beforeScoped.EnumerateArray(), item => item.GetProperty("id").GetGuid() == conversationId);

        var members = await owner.GetFromJsonAsync<JsonElement>($"/api/knowledge-bases/{knowledgeBaseId}/members");
        var memberUserId = members.EnumerateArray()
            .Single(item => string.Equals(item.GetProperty("email").GetString(), memberEmail, StringComparison.OrdinalIgnoreCase))
            .GetProperty("id").GetGuid();

        using var removeMemberResponse = await owner.DeleteAsync($"/api/knowledge-bases/{knowledgeBaseId}/members/{memberUserId}");
        removeMemberResponse.EnsureSuccessStatusCode();

        var after = await member.GetFromJsonAsync<JsonElement>("/api/conversations");
        Assert.DoesNotContain(after.EnumerateArray(), item => item.GetProperty("id").GetGuid() == conversationId);

        using var scopedAfter = await member.GetAsync(
            $"/api/conversations?knowledgeBaseId={knowledgeBaseId}");
        Assert.Equal(HttpStatusCode.Forbidden, scopedAfter.StatusCode);

        using var messagesResponse = await member.GetAsync($"/api/conversations/{conversationId}/messages");
        Assert.Equal(HttpStatusCode.Forbidden, messagesResponse.StatusCode);
    }

    private static async Task<TestAuth> RegisterAsync(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email, password = "Passw0rd!123", displayName = "Test User" });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new TestAuth(
            json.RootElement.GetProperty("accessToken").GetString()!,
            json.RootElement.GetProperty("refreshToken").GetString()!);
    }

    private sealed record TestAuth(string AccessToken, string RefreshToken);
}
