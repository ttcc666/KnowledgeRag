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
            "rag.run.completed",
            "{\"id\":\"5b88e21c-668d-44f0-9d72-624b3cd50ce8\",\"status\":\"completed\",\"sourceCount\":6,\"latencyMs\":36065,\"estimatedInputTokens\":5985,\"estimatedOutputTokens\":637,\"estimatedCostUsd\":0.003308,\"answerModel\":\"deepseek-ai/DeepSeek-V3\",\"agentMode\":\"specialists\"}",
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
    public void Formatter_RagCompleted_RendersReadableSummaryWithoutRawJson()
    {
        var (_, message) = WebhookChannelMessageFormatter.Format(CreateDelivery());

        Assert.Contains("RAG 运行完成", message);
        Assert.Contains("引用来源：6 个", message);
        Assert.Contains("耗时：36.1 秒", message);
        Assert.Contains("Token：5,985 输入 / 637 输出", message);
        Assert.Contains("估算成本：$0.003308", message);
        Assert.Contains("回答模型：deepseek-ai/DeepSeek-V3", message);
        Assert.Contains("Run ID：5b88e21c", message);
        Assert.DoesNotContain("{", message);
        Assert.DoesNotContain("\"sourceCount\"", message);
    }

    [Fact]
    public void Formatter_TestEvent_RendersFriendlyConfirmation()
    {
        var delivery = new WebhookDeliveryRequested(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Guid.NewGuid(), Guid.NewGuid(), "webhook.test",
            "{\"message\":\"KnowledgeRag webhook test\",\"at\":\"2026-10-28T08:20:40.9221159+00:00\"}",
            DateTimeOffset.Parse("2026-10-28T08:20:40Z"));

        var (_, message) = WebhookChannelMessageFormatter.Format(delivery);

        Assert.Contains("KnowledgeRag 通知测试", message);
        Assert.Contains("KnowledgeRag webhook test", message);
        Assert.Contains("2026-10-28 08:20:40 UTC", message);
        Assert.DoesNotContain("\\u002B", message);
        Assert.DoesNotContain("{", message);
    }

    [Fact]
    public void Formatter_EvaluationCompleted_RendersMetricsAndRegressionWarning()
    {
        var delivery = new WebhookDeliveryRequested(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "evaluation.completed",
            "{\"id\":\"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\",\"passRate\":0.9,\"groundednessRate\":0.95,\"citationValidityRate\":1.0,\"noAnswerAccuracy\":0.8,\"averageLatencyMs\":1750,\"passRateRegression\":true,\"groundednessRegression\":false,\"latencyRegression\":true}",
            DateTimeOffset.Parse("2026-10-28T12:00:00Z"));

        var (_, message) = WebhookChannelMessageFormatter.Format(delivery);

        Assert.Contains("Evaluation 完成（检测到回归）", message);
        Assert.Contains("通过率：90.0%", message);
        Assert.Contains("Groundedness：95.0%", message);
        Assert.Contains("Citation validity：100.0%", message);
        Assert.Contains("No-answer accuracy：80.0%", message);
        Assert.Contains("平均耗时：1.8 秒", message);
        Assert.Contains("回归项：通过率、延迟", message);
        Assert.DoesNotContain("{", message);
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
