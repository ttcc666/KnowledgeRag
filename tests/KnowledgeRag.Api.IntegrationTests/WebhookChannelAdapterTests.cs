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
            "{\"id\":\"5b88e21c-668d-44f0-9d72-624b3cd50ce8\",\"conversationId\":\"7bbc659f-53ae-470e-bcd2-7c79cf0a4ce1\",\"status\":\"completed\",\"sourceCount\":6,\"latencyMs\":36065,\"estimatedInputTokens\":5985,\"estimatedOutputTokens\":637,\"estimatedCostUsd\":0.003308,\"answerModel\":\"deepseek-ai/DeepSeek-V3\",\"agentMode\":\"specialists\"}",
            DateTimeOffset.Parse("2026-10-28T12:00:00Z"));

    [Fact]
    public async Task SlackAdapter_UsesBlockKit()
    {
        using var request = CreateProviderRequest("slack");
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        var blocks = body.RootElement.GetProperty("blocks");

        Assert.Equal("header", blocks[0].GetProperty("type").GetString());
        Assert.Contains(blocks.EnumerateArray(), block => block.GetProperty("type").GetString() == "section" && block.TryGetProperty("fields", out _));
        Assert.True(body.RootElement.TryGetProperty("text", out _));
    }

    [Fact]
    public async Task DiscordAdapter_UsesEmbedAndDisablesMentions()
    {
        using var request = CreateProviderRequest("discord");
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());

        var embed = body.RootElement.GetProperty("embeds")[0];
        Assert.Contains("RAG 问答运行完成", embed.GetProperty("title").GetString());
        Assert.True(embed.GetProperty("fields").GetArrayLength() >= 4);
        Assert.Empty(body.RootElement.GetProperty("allowed_mentions").GetProperty("parse").EnumerateArray());
    }

    [Fact]
    public async Task TeamsAdapter_UsesAdaptiveCardWithFactSet()
    {
        using var request = CreateProviderRequest("teams");
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());

        var attachment = body.RootElement.GetProperty("attachments")[0];
        Assert.Equal("application/vnd.microsoft.card.adaptive", attachment.GetProperty("contentType").GetString());
        var cardBody = attachment.GetProperty("content").GetProperty("body");
        Assert.Contains(cardBody.EnumerateArray(), element => element.GetProperty("type").GetString() == "FactSet");
    }

    [Fact]
    public async Task FeishuAdapter_UsesInteractiveCard()
    {
        using var request = CreateProviderRequest("feishu");
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());

        Assert.Equal("interactive", body.RootElement.GetProperty("msg_type").GetString());
        var card = body.RootElement.GetProperty("card");
        Assert.Equal("turquoise", card.GetProperty("header").GetProperty("template").GetString());
        Assert.Contains("RAG 问答运行完成", card.GetProperty("header").GetProperty("title").GetProperty("content").GetString());
        Assert.Contains(card.GetProperty("elements").EnumerateArray(), element => element.GetProperty("tag").GetString() == "div");
    }

    [Fact]
    public async Task FeishuAdapter_TestEvent_UsesLegacyCompatibleDivSummary()
    {
        var delivery = new WebhookDeliveryRequested(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Guid.NewGuid(), Guid.NewGuid(), "webhook.test",
            "{\"message\":\"KnowledgeRag webhook test\",\"at\":\"2026-10-28T08:20:40.9221159+00:00\"}",
            DateTimeOffset.Parse("2026-10-28T08:20:40Z"));
        using var request = WebhookChannelAdapterRegistry.Get("feishu").CreateRequest(
            new Uri("https://example.com/hook"), delivery, "{}", "fallback", null);
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());

        var elements = body.RootElement.GetProperty("card").GetProperty("elements");
        var summary = elements[0];
        Assert.Equal("div", summary.GetProperty("tag").GetString());
        Assert.Equal("lark_md", summary.GetProperty("text").GetProperty("tag").GetString());
        Assert.Contains("Webhook Automation", summary.GetProperty("text").GetProperty("content").GetString());
        Assert.DoesNotContain(elements.EnumerateArray(), element => element.GetProperty("tag").GetString() == "markdown");
    }

    [Fact]
    public async Task DingTalkAdapter_UsesMarkdownMessage()
    {
        using var request = CreateProviderRequest("dingtalk");
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());

        Assert.Equal("markdown", body.RootElement.GetProperty("msgtype").GetString());
        Assert.Contains("响应耗时", body.RootElement.GetProperty("markdown").GetProperty("text").GetString());
    }

    [Fact]
    public async Task WeComAdapter_UsesMarkdownMessage()
    {
        using var request = CreateProviderRequest("wecom");
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());

        Assert.Equal("markdown", body.RootElement.GetProperty("msgtype").GetString());
        Assert.Contains("引用召回", body.RootElement.GetProperty("markdown").GetProperty("content").GetString());
    }

    [Fact]
    public void Formatter_RagCompleted_RendersReadableSummaryWithoutRawJson()
    {
        var (_, message) = WebhookChannelMessageFormatter.Format(CreateDelivery());

        Assert.Contains("RAG 问答运行完成", message);
        Assert.Contains("引用召回：6 篇文档", message);
        Assert.Contains("响应耗时：36.1 秒", message);
        Assert.Contains("Token 消耗：入 5,985 / 出 637", message);
        Assert.Contains("估算费用：$0.003308 USD", message);
        Assert.Contains("问答模型：deepseek-ai/DeepSeek-V3", message);
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

        Assert.Contains("KnowledgeRag 连通性测试", message);
        Assert.Contains("KnowledgeRag webhook test", message);
        Assert.Contains("Delivery ID：44444444", message);
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

        Assert.Contains("RAG Evaluation 完成（检测到回归）", message);
        Assert.Contains("通过率：90.0%", message);
        Assert.Contains("Groundedness：95.0%", message);
        Assert.Contains("Citation validity：100.0%", message);
        Assert.Contains("No-answer accuracy：80.0%", message);
        Assert.Contains("平均耗时：1.8 秒", message);
        Assert.Contains("检测到回归：通过率、延迟", message);
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

    private static HttpRequestMessage CreateProviderRequest(string provider)
    {
        var delivery = CreateDelivery();
        return WebhookChannelAdapterRegistry.Get(provider).CreateRequest(
            new Uri("https://example.com/hook"), delivery, "{\"id\":\"event-1\"}", "fallback", null);
    }
}
