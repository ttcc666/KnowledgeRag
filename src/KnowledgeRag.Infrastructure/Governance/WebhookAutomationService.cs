using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNetCore.CAP;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeRag.Infrastructure.Governance;

public sealed class WebhookAutomationService(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    WebhookSecretProtector secretProtector,
    ICapPublisher capPublisher)
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Queues one CAP message per matching subscription. No outbound HTTP request is made on
    /// the caller's critical path. PostgreSQL-backed CAP persists the message in its outbox;
    /// SQLite keeps the existing in-memory CAP development behavior.
    /// </summary>
    public async Task DispatchAsync(Guid tenantId, string eventType, object payload, CancellationToken ct = default)
    {
        var hooks = await db.Set<WebhookSubscriptionEntity>().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .Select(x => new { x.Id, x.EventTypes })
            .ToListAsync(ct);
        var payloadJson = JsonSerializer.Serialize(payload, WebJson);
        var eventId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        foreach (var hook in hooks.Where(x => Matches(x.EventTypes, eventType)))
        {
            ct.ThrowIfCancellationRequested();
            await capPublisher.PublishAsync(WebhookEventTopics.DeliveryRequested,
                new WebhookDeliveryRequested(eventId, hook.Id, tenantId, eventType, payloadJson, createdAt));
        }
    }

    public async Task TestAsync(Guid tenantId, Guid webhookId, CancellationToken ct = default)
    {
        var hook = await db.Set<WebhookSubscriptionEntity>().SingleOrDefaultAsync(x => x.Id == webhookId && x.TenantId == tenantId, ct)
                   ?? throw new KeyNotFoundException("Webhook 不存在。");
        var payload = JsonSerializer.Serialize(new { message = "KnowledgeRag webhook test", at = DateTimeOffset.UtcNow }, WebJson);
        await DeliverAsync(hook,
            new WebhookDeliveryRequested(Guid.NewGuid(), hook.Id, tenantId, "webhook.test", payload, DateTimeOffset.UtcNow), ct);
    }

    public async Task DeliverRequestedAsync(WebhookDeliveryRequested request, CancellationToken ct = default)
    {
        var hook = await db.Set<WebhookSubscriptionEntity>()
            .SingleOrDefaultAsync(x => x.Id == request.SubscriptionId && x.TenantId == request.TenantId, ct);
        if (hook is null || !hook.IsActive || !Matches(hook.EventTypes, request.EventType)) return;
        await DeliverAsync(hook, request, ct);
    }

    private async Task DeliverAsync(WebhookSubscriptionEntity hook, WebhookDeliveryRequested delivery, CancellationToken ct)
    {
        Uri uri;
        try
        {
            uri = WebhookUrlPolicy.ValidateUrl(hook.Url);
        }
        catch (Exception ex) when (ex is InvalidOperationException or WebhookDestinationBlockedException)
        {
            await RecordDeliveryAsync(hook, null, SafeError(ex), ct);
            return;
        }

        using var payloadDocument = JsonDocument.Parse(delivery.PayloadJson);
        var envelope = new
        {
            id = delivery.EventId,
            type = delivery.EventType,
            createdAt = delivery.CreatedAt,
            data = payloadDocument.RootElement.Clone()
        };
        var json = JsonSerializer.Serialize(envelope, WebJson);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("X-KnowledgeRag-Event", delivery.EventType);
        request.Headers.TryAddWithoutValidation("X-KnowledgeRag-Delivery", delivery.EventId.ToString("D"));
        if (!string.IsNullOrWhiteSpace(hook.Secret))
        {
            var secret = secretProtector.Unprotect(hook.Secret);
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
            request.Headers.TryAddWithoutValidation("X-KnowledgeRag-Signature", "sha256=" + signature);
        }

        try
        {
            var client = httpClientFactory.CreateClient("governance-webhook");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var error = response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}";
            await RecordDeliveryAsync(hook, (int)response.StatusCode, error, ct);
            if (!response.IsSuccessStatusCode && IsRetryable(response.StatusCode))
                throw new HttpRequestException($"Webhook delivery failed with HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (WebhookUrlPolicy.IsBlocked(ex))
        {
            await RecordDeliveryAsync(hook, null, "Webhook 目标被 SSRF 安全策略阻止。", CancellationToken.None);
        }
        catch (Exception ex)
        {
            await RecordDeliveryAsync(hook, null, SafeError(ex), CancellationToken.None);
            throw;
        }
    }

    private async Task RecordDeliveryAsync(WebhookSubscriptionEntity hook, int? statusCode, string? error, CancellationToken ct)
    {
        hook.LastStatusCode = statusCode;
        hook.LastError = error;
        hook.LastDeliveredAt = DateTimeOffset.UtcNow;
        hook.UpdatedAt = hook.LastDeliveredAt.Value;
        await db.SaveChangesAsync(ct);
    }

    private static bool IsRetryable(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.RequestTimeout or (HttpStatusCode)429 || (int)statusCode >= 500;

    private static string SafeError(Exception ex)
        => ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000];

    private static bool Matches(string configured, string eventType)
        => configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x => x == "*" || string.Equals(x, eventType, StringComparison.OrdinalIgnoreCase));
}
