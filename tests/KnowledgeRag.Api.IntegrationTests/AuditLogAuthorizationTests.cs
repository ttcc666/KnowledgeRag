using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeRag.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class AuditLogAuthorizationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task AuditLog_RecordsAuthenticatedReadDenial_ButNotSuccessfulReads()
    {
        using var owner = factory.CreateClient();
        var ownerToken = await RegisterAsync(owner, $"audit-owner-{Guid.NewGuid():N}@example.test");
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        using var createKb = await owner.PostAsJsonAsync("/api/knowledge-bases", new { name = "Audit read denial KB" });
        createKb.EnsureSuccessStatusCode();
        using var kbJson = JsonDocument.Parse(await createKb.Content.ReadAsStringAsync());
        var knowledgeBaseId = kbJson.RootElement.GetProperty("id").GetGuid();

        using var member = factory.CreateClient();
        var memberEmail = $"audit-member-{Guid.NewGuid():N}@example.test";
        var memberToken = await RegisterAsync(member, memberEmail);
        member.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        var memberProfile = await member.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var memberId = memberProfile.GetProperty("id").GetGuid();

        using var addMember = await owner.PutAsJsonAsync(
            $"/api/knowledge-bases/{knowledgeBaseId}/members",
            new { email = memberEmail, role = "Viewer" });
        addMember.EnsureSuccessStatusCode();

        using var allowedRead = await member.GetAsync($"/api/knowledge-bases/{knowledgeBaseId}");
        Assert.Equal(HttpStatusCode.OK, allowedRead.StatusCode);

        using var removeMember = await owner.DeleteAsync($"/api/knowledge-bases/{knowledgeBaseId}/members/{memberId}");
        removeMember.EnsureSuccessStatusCode();

        using var deniedRead = await member.GetAsync($"/api/knowledge-bases/{knowledgeBaseId}");
        Assert.Equal(HttpStatusCode.Forbidden, deniedRead.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditRows = await db.AuditLogs.AsNoTracking()
            .Where(x => x.UserId == memberId && x.Summary == $"GET /api/knowledge-bases/{knowledgeBaseId}")
            .ToListAsync();

        Assert.Contains(auditRows, row => row.StatusCode == StatusCodes.Status403Forbidden);
        Assert.DoesNotContain(auditRows, row => row.StatusCode < 400);
    }

    private static async Task<string> RegisterAsync(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email, password = "Passw0rd!123", displayName = "Audit Test" });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("accessToken").GetString()!;
    }
}
