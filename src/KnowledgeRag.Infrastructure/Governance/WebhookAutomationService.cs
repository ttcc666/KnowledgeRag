using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeRag.Infrastructure.Governance;

public sealed class WebhookAutomationService(AppDbContext db, IHttpClientFactory httpClientFactory)
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public async Task DispatchAsync(Guid tenantId, string eventType, object payload, CancellationToken ct = default)
    {
        var hooks = await db.Set<WebhookSubscriptionEntity>()
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .ToListAsync(ct);
        foreach (var hook in hooks.Where(x => Matches(x.EventTypes, eventType)))
            await DeliverAsync(hook, eventType, payload, ct);
    }

    public async Task TestAsync(Guid tenantId, Guid webhookId, CancellationToken ct = default)
    {
        var hook = await db.Set<WebhookSubscriptionEntity>().SingleOrDefaultAsync(x => x.Id == webhookId && x.TenantId == tenantId, ct)
                   ?? throw new KeyNotFoundException("Webhook 不存在。");
        await DeliverAsync(hook, "webhook.test", new { message = "KnowledgeRag webhook test", at = DateTimeOffset.UtcNow }, ct);
    }

    private async Task DeliverAsync(WebhookSubscriptionEntity hook, string eventType, object payload, CancellationToken ct)
    {
        var envelope = new { id = Guid.NewGuid(), type = eventType, createdAt = DateTimeOffset.UtcNow, data = payload };
        var json = JsonSerializer.Serialize(envelope, WebJson);
        using var request = new HttpRequestMessage(HttpMethod.Post, hook.Url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("X-KnowledgeRag-Event", eventType);
        if (!string.IsNullOrWhiteSpace(hook.Secret))
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(hook.Secret));
            var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
            request.Headers.TryAddWithoutValidation("X-KnowledgeRag-Signature", "sha256=" + signature);
        }

        try
        {
            var client = httpClientFactory.CreateClient("governance-webhook");
            using var response = await client.SendAsync(request, ct);
            hook.LastStatusCode = (int)response.StatusCode;
            hook.LastError = response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            hook.LastStatusCode = null;
            hook.LastError = ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000];
        }
        hook.LastDeliveredAt = DateTimeOffset.UtcNow;
        hook.UpdatedAt = hook.LastDeliveredAt.Value;
        await db.SaveChangesAsync(ct);
    }

    private static bool Matches(string configured, string eventType)
        => configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x => x == "*" || string.Equals(x, eventType, StringComparison.OrdinalIgnoreCase));
}
