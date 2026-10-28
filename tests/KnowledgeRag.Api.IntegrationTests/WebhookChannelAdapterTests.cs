using System.Text.Json;
using KnowledgeRag.Infrastructure.Governance;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class WebhookChannelAdapterTests
{
    private static WebhookDeliveryRequested CreateDelivery()
        => new(Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "rag.run.completed", "{\"answer\":\"Policy answer\",\"sourceCount\":2}",
            DateTimeOffset.Parse("2026-10-28T12:00:00Z"));

    [Theory]
    [InlineData("slack", "text")]
    [InlineData("discord", "content")]
    [InlineData("teams", "attachments")]
    [InlineData("feishu", "msg_type")]
    [InlineData("dingtalk", "msgtype")]
    [InlineData("wecom", "msgtype")]
    public async Task ProviderAdapters_RenderPlatformSpecificPayloads(string provider, string expectedProperty)
    {
        var delivery = CreateDelivery();
        using var request = WebhookChannelAdapterRegistry.Get(provider).CreateRequest(
            new Uri("https://example.com/hook"), delivery, "{\"id\":\"event-1\"}", "KnowledgeRag test message", null);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("application/json", request.Content?.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        Assert.True(body.RootElement.TryGetProperty(expectedProperty, out _));
    }

    [Fact]
    public async Task CustomAdapter_PreservesEnvelopeAndHmacSignature()
    {
        var delivery = CreateDelivery();
        const string envelope = "{\"id\":\"event-1\",\"type\":\"rag.run.completed\"}";
        using var request = WebhookChannelAdapterRegistry.Get("custom").CreateRequest(
            new Uri("https://example.com/hook"), delivery, envelope, "ignored", "shared-secret");
        Assert.Equal(envelope, await request.Content!.ReadAsStringAsync());
        Assert.Equal("rag.run.completed", request.Headers.GetValues("X-KnowledgeRag-Event").Single());
        Assert.Equal(delivery.EventId.ToString("D"), request.Headers.GetValues("X-KnowledgeRag-Delivery").Single());
        Assert.StartsWith("sha256=", request.Headers.GetValues("X-KnowledgeRag-Signature").Single());
    }

    [Fact]
    public void FeishuAndDingTalk_ApplyConfiguredSigningSecrets()
    {
        var delivery = CreateDelivery();
        using var feishu = WebhookChannelAdapterRegistry.Get("feishu").CreateRequest(
            new Uri("https://example.com/feishu"), delivery, "{}", "message", "feishu-secret");
        using var feishuBody = JsonDocument.Parse(feishu.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        Assert.True(feishuBody.RootElement.TryGetProperty("timestamp", out _));
        Assert.True(feishuBody.RootElement.TryGetProperty("sign", out _));
        using var dingtalk = WebhookChannelAdapterRegistry.Get("dingtalk").CreateRequest(
            new Uri("https://example.com/dingtalk?access_token=token"), delivery, "{}", "message", "ding-secret");
        Assert.Contains("timestamp=", dingtalk.RequestUri!.Query);
        Assert.Contains("sign=", dingtalk.RequestUri.Query);
    }

    [Fact]
    public void MissingProviderDefaultsToCustom_AndUnknownProviderIsRejected()
    {
        Assert.Equal("custom", WebhookChannelAdapterRegistry.NormalizeProvider(null));
        Assert.Equal("slack", WebhookChannelAdapterRegistry.NormalizeProvider(" Slack "));
        Assert.Throws<InvalidOperationException>(() => WebhookChannelAdapterRegistry.NormalizeProvider("not-a-provider"));
    }
}
