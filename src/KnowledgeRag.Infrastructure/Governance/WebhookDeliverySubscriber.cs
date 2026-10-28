using DotNetCore.CAP;

namespace KnowledgeRag.Infrastructure.Governance;

public static class WebhookEventTopics
{
    public const string DeliveryRequested = "knowledge-rag.webhook.delivery.requested";
}

public sealed record WebhookDeliveryRequested(
    Guid EventId,
    Guid SubscriptionId,
    Guid TenantId,
    string EventType,
    string PayloadJson,
    DateTimeOffset CreatedAt);

public sealed class WebhookDeliverySubscriber(WebhookAutomationService webhooks) : ICapSubscribe
{
    [CapSubscribe(WebhookEventTopics.DeliveryRequested)]
    public Task HandleAsync(WebhookDeliveryRequested request)
        => webhooks.DeliverRequestedAsync(request, CancellationToken.None);
}
