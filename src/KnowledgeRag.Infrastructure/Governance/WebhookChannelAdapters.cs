using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KnowledgeRag.Infrastructure.Governance;

public sealed record WebhookProviderDescriptor(string Id, string Name, string Description, string UrlPlaceholder, string SecretLabel, string SecretHint);

public sealed record WebhookNotificationField(string Label, string Value, bool Short = true);

public sealed record WebhookNotificationPresentation(
    string Title,
    string? Summary,
    IReadOnlyList<WebhookNotificationField> Fields,
    string Footer,
    string Severity)
{
    public string ToPlainText()
    {
        var lines = new List<string> { $"{SeverityEmoji(Severity)} {Title}" };
        if (!string.IsNullOrWhiteSpace(Summary)) lines.Add(Summary!);
        lines.AddRange(Fields.Select(field => $"{field.Label}：{field.Value}"));
        if (!string.IsNullOrWhiteSpace(Footer)) lines.Add(Footer);
        return string.Join("\n", lines);
    }

    private static string SeverityEmoji(string severity)
        => severity.ToLowerInvariant() switch
        {
            "success" => "✅",
            "warning" => "⚠️",
            "error" => "❌",
            _ => "🔔"
        };
}

/// <summary>Converts KnowledgeRag events to provider-specific requests without changing event dispatch.</summary>
public interface IWebhookChannelAdapter
{
    WebhookProviderDescriptor Descriptor { get; }
    string Id { get; }
    HttpRequestMessage CreateRequest(Uri destination, WebhookDeliveryRequested delivery, string envelopeJson, string message, string? secret);
}

public static class WebhookChannelAdapterRegistry
{
    private static readonly IReadOnlyDictionary<string, IWebhookChannelAdapter> AdapterMap =
        new IWebhookChannelAdapter[]
        {
            new CustomWebhookChannelAdapter(), new SlackWebhookChannelAdapter(), new DiscordWebhookChannelAdapter(),
            new TeamsWebhookChannelAdapter(), new FeishuWebhookChannelAdapter(), new DingTalkWebhookChannelAdapter(),
            new WeComWebhookChannelAdapter(),
        }.ToDictionary(adapter => adapter.Id, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<WebhookProviderDescriptor> Providers { get; } = AdapterMap.Values.Select(x => x.Descriptor).ToArray();

    public static string NormalizeProvider(string? provider)
    {
        var normalized = string.IsNullOrWhiteSpace(provider) ? "custom" : provider.Trim();
        return AdapterMap.TryGetValue(normalized, out var adapter)
            ? adapter.Id
            : throw new InvalidOperationException($"不支持的 Webhook Provider：{normalized}。");
    }

    public static IWebhookChannelAdapter Get(string? provider)
    {
        var normalized = string.IsNullOrWhiteSpace(provider) ? "custom" : provider.Trim();
        return AdapterMap.TryGetValue(normalized, out var adapter)
            ? adapter
            : throw new InvalidOperationException($"不支持的 Webhook Provider：{normalized}。");
    }
}

public static class WebhookChannelMessageFormatter
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static (string EnvelopeJson, string Message) Format(WebhookDeliveryRequested delivery)
    {
        using var payloadDocument = JsonDocument.Parse(delivery.PayloadJson);
        var payload = payloadDocument.RootElement;
        var envelopeJson = JsonSerializer.Serialize(new
        {
            id = delivery.EventId, type = delivery.EventType, createdAt = delivery.CreatedAt,
            data = payload.Clone()
        }, WebJson);
        var presentation = BuildPresentation(delivery, payload);
        return (envelopeJson, presentation.ToPlainText());
    }

    public static WebhookNotificationPresentation Present(WebhookDeliveryRequested delivery)
    {
        using var payloadDocument = JsonDocument.Parse(delivery.PayloadJson);
        return BuildPresentation(delivery, payloadDocument.RootElement);
    }

    private static WebhookNotificationPresentation BuildPresentation(WebhookDeliveryRequested delivery, JsonElement payload)
        => delivery.EventType.ToLowerInvariant() switch
        {
            "webhook.test" => PresentTest(delivery, payload),
            "rag.run.completed" => PresentRagRunCompleted(delivery, payload),
            "evaluation.completed" => PresentEvaluationCompleted(delivery, payload),
            _ => PresentGeneric(delivery, payload)
        };

    private static WebhookNotificationPresentation PresentTest(WebhookDeliveryRequested delivery, JsonElement payload)
    {
        var configuredMessage = ReadString(payload, "message");
        return new WebhookNotificationPresentation(
            "KnowledgeRag 连通性测试",
            string.IsNullOrWhiteSpace(configuredMessage)
                ? "这是一条来自 Webhook Automation 的测试事件，消息通道配置已生效。"
                : $"这是一条来自 Webhook Automation 的测试事件：\n{configuredMessage}",
            Array.Empty<WebhookNotificationField>(),
            $"测试触发时间：{FormatTime(delivery.CreatedAt)} · Delivery ID：{ShortId(delivery.EventId)}",
            "success");
    }

    private static WebhookNotificationPresentation PresentRagRunCompleted(WebhookDeliveryRequested delivery, JsonElement payload)
    {
        var status = ReadString(payload, "status") ?? "completed";
        var runId = ReadString(payload, "id");
        var conversationId = ReadString(payload, "conversationId");
        var sourceCount = ReadInt64(payload, "sourceCount");
        var latencyMs = ReadDouble(payload, "latencyMs");
        var inputTokens = ReadInt64(payload, "estimatedInputTokens");
        var outputTokens = ReadInt64(payload, "estimatedOutputTokens");
        var cost = ReadDecimal(payload, "estimatedCostUsd");
        var model = ReadString(payload, "answerModel");
        var agentMode = ReadString(payload, "agentMode");
        var success = status.Equals("completed", StringComparison.OrdinalIgnoreCase);

        var fields = new List<WebhookNotificationField>();
        if (latencyMs.HasValue) fields.Add(new("响应耗时", FormatDuration(latencyMs.Value)));
        if (sourceCount.HasValue) fields.Add(new("引用召回", $"{sourceCount.Value:N0} 篇文档"));
        if (cost.HasValue) fields.Add(new("估算费用", $"${cost.Value.ToString("0.######", CultureInfo.InvariantCulture)} USD"));
        if (inputTokens.HasValue || outputTokens.HasValue)
            fields.Add(new("Token 消耗", $"入 {FormatCount(inputTokens)} / 出 {FormatCount(outputTokens)}"));
        if (!string.IsNullOrWhiteSpace(model)) fields.Add(new("问答模型", model!));
        if (!string.IsNullOrWhiteSpace(agentMode)) fields.Add(new("编排模式", agentMode!));

        var footerParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(conversationId)) footerParts.Add($"会话 ID：{ShortId(conversationId)}");
        if (!string.IsNullOrWhiteSpace(runId)) footerParts.Add($"Run ID：{ShortId(runId)}");
        footerParts.Add($"记录时间：{FormatTime(delivery.CreatedAt)}");

        return new WebhookNotificationPresentation(
            success ? "RAG 问答运行完成" : $"RAG 问答运行 {DisplayStatus(status)}",
            success ? null : $"当前运行状态：{DisplayStatus(status)}",
            fields,
            string.Join(" · ", footerParts),
            success ? "success" : status.Equals("failed", StringComparison.OrdinalIgnoreCase) ? "error" : "warning");
    }

    private static WebhookNotificationPresentation PresentEvaluationCompleted(WebhookDeliveryRequested delivery, JsonElement payload)
    {
        var passRate = ReadDouble(payload, "passRate");
        var groundednessRate = ReadDouble(payload, "groundednessRate");
        var citationValidityRate = ReadDouble(payload, "citationValidityRate");
        var noAnswerAccuracy = ReadDouble(payload, "noAnswerAccuracy");
        var latencyMs = ReadDouble(payload, "averageLatencyMs");
        var runId = ReadString(payload, "id");
        var passRateRegression = ReadBoolean(payload, "passRateRegression");
        var groundednessRegression = ReadBoolean(payload, "groundednessRegression");
        var latencyRegression = ReadBoolean(payload, "latencyRegression");
        var hasRegression = passRateRegression == true || groundednessRegression == true || latencyRegression == true;

        var fields = new List<WebhookNotificationField>();
        if (passRate.HasValue) fields.Add(new("通过率", FormatPercent(passRate.Value)));
        if (groundednessRate.HasValue) fields.Add(new("Groundedness", FormatPercent(groundednessRate.Value)));
        if (citationValidityRate.HasValue) fields.Add(new("Citation validity", FormatPercent(citationValidityRate.Value)));
        if (noAnswerAccuracy.HasValue) fields.Add(new("No-answer accuracy", FormatPercent(noAnswerAccuracy.Value)));
        if (latencyMs.HasValue) fields.Add(new("平均耗时", FormatDuration(latencyMs.Value)));

        string? summary = null;
        if (hasRegression)
        {
            var regressions = new List<string>();
            if (passRateRegression == true) regressions.Add("通过率");
            if (groundednessRegression == true) regressions.Add("Groundedness");
            if (latencyRegression == true) regressions.Add("延迟");
            summary = $"检测到回归：{string.Join("、", regressions)}";
        }

        var footer = new List<string>();
        if (!string.IsNullOrWhiteSpace(runId)) footer.Add($"Run ID：{ShortId(runId)}");
        footer.Add($"记录时间：{FormatTime(delivery.CreatedAt)}");
        return new WebhookNotificationPresentation(
            hasRegression ? "RAG Evaluation 完成（检测到回归）" : "RAG Evaluation 完成",
            summary,
            fields,
            string.Join(" · ", footer),
            hasRegression ? "warning" : "success");
    }

    private static WebhookNotificationPresentation PresentGeneric(WebhookDeliveryRequested delivery, JsonElement payload)
    {
        var fields = new List<WebhookNotificationField>();
        if (payload.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in payload.EnumerateObject())
            {
                if (fields.Count >= 8) break;
                var value = FormatPrimitive(property.Value);
                if (!string.IsNullOrWhiteSpace(value)) fields.Add(new(Humanize(property.Name), LimitValue(value!, 500)));
            }
        }
        return new WebhookNotificationPresentation(
            $"KnowledgeRag · {delivery.EventType}",
            null,
            fields,
            $"事件时间：{FormatTime(delivery.CreatedAt)} · Delivery ID：{ShortId(delivery.EventId)}",
            "info");
    }

    private static string? ReadString(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
            _ => null
        };
    }

    private static long? ReadInt64(JsonElement payload, string propertyName)
        => payload.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result)
            ? result
            : null;

    private static double? ReadDouble(JsonElement payload, string propertyName)
        => payload.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var result)
            ? result
            : null;

    private static decimal? ReadDecimal(JsonElement payload, string propertyName)
        => payload.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var result)
            ? result
            : null;

    private static bool? ReadBoolean(JsonElement payload, string propertyName)
        => payload.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static string? FormatPrimitive(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
            _ => null
        };

    private static string Humanize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "详情";
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            if (i > 0 && char.IsUpper(ch) && !char.IsUpper(name[i - 1])) builder.Append(' ');
            builder.Append(ch);
        }
        return builder.ToString();
    }

    private static string DisplayStatus(string status)
        => status.ToLowerInvariant() switch
        {
            "completed" => "已完成",
            "failed" => "失败",
            "cancelled" => "已取消",
            "running" => "运行中",
            _ => status
        };

    private static string FormatCount(long? value)
        => value.HasValue ? value.Value.ToString("N0", CultureInfo.InvariantCulture) : "—";

    private static string FormatDuration(double milliseconds)
        => milliseconds >= 1000
            ? $"{(milliseconds / 1000d).ToString("0.0", CultureInfo.InvariantCulture)} 秒"
            : $"{milliseconds.ToString("0", CultureInfo.InvariantCulture)} 毫秒";

    private static string FormatPercent(double value)
        => $"{(value * 100d).ToString("0.0", CultureInfo.InvariantCulture)}%";

    private static string FormatTime(DateTimeOffset value)
        => value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);

    private static string LimitValue(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "…";

    private static string ShortId(Guid value) => value.ToString("D")[..8];
    private static string ShortId(string value) => value.Length <= 8 ? value : value[..8];
}

internal static class WebhookChannelAdapterHelpers
{
    internal static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    internal static HttpRequestMessage JsonPost(Uri destination, object payload)
        => new(HttpMethod.Post, destination)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, WebJson), Encoding.UTF8, "application/json")
        };

    internal static string Limit(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "…";

    internal static string EscapeSlack(string value)
        => value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    internal static string EscapeAngleMarkup(string value)
        => value.Replace("<", "‹", StringComparison.Ordinal).Replace(">", "›", StringComparison.Ordinal);

    internal static string SeverityEmoji(string severity)
        => severity.ToLowerInvariant() switch
        {
            "success" => "✅",
            "warning" => "⚠️",
            "error" => "❌",
            _ => "🔔"
        };

    internal static int DiscordColor(string severity)
        => severity.ToLowerInvariant() switch
        {
            "success" => 0x2ECC71,
            "warning" => 0xF1C40F,
            "error" => 0xE74C3C,
            _ => 0x3498DB
        };

    internal static string FeishuTemplate(string severity)
        => severity.ToLowerInvariant() switch
        {
            "success" => "turquoise",
            "warning" => "orange",
            "error" => "red",
            _ => "blue"
        };

    internal static string SignDingTalk(string timestamp, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}\n{secret}")));
    }

    internal static string SignFeishu(string timestamp, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes($"{timestamp}\n{secret}"));
        return Convert.ToBase64String(hmac.ComputeHash(Array.Empty<byte>()));
    }
}

internal sealed class CustomWebhookChannelAdapter : IWebhookChannelAdapter
{
    public string Id => "custom";
    public WebhookProviderDescriptor Descriptor { get; } = new("custom", "自定义 HTTP Webhook",
        "通用 JSON 事件信封；可选 HMAC-SHA256 签名，适合自建服务和其他兼容 HTTP 的系统。",
        "https://example.com/hooks/rag", "HMAC 签名密钥（可选）", "填写后生成 X-KnowledgeRag-Signature: sha256=...");

    public HttpRequestMessage CreateRequest(Uri destination, WebhookDeliveryRequested delivery, string envelopeJson, string message, string? secret)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, destination)
        {
            Content = new StringContent(envelopeJson, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("X-KnowledgeRag-Event", delivery.EventType);
        request.Headers.TryAddWithoutValidation("X-KnowledgeRag-Delivery", delivery.EventId.ToString("D"));
        if (!string.IsNullOrWhiteSpace(secret))
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(envelopeJson))).ToLowerInvariant();
            request.Headers.TryAddWithoutValidation("X-KnowledgeRag-Signature", "sha256=" + signature);
        }
        return request;
    }
}

internal sealed class SlackWebhookChannelAdapter : IWebhookChannelAdapter
{
    public string Id => "slack";
    public WebhookProviderDescriptor Descriptor { get; } = new("slack", "Slack",
        "发送到 Slack Incoming Webhook，使用 Block Kit 原生布局展示标题、指标与上下文。",
        "https://hooks.slack.com/services/...", "无需填写", "Slack 使用 Incoming Webhook URL 作为凭据。");

    public HttpRequestMessage CreateRequest(Uri destination, WebhookDeliveryRequested delivery, string envelopeJson, string message, string? secret)
    {
        var card = WebhookChannelMessageFormatter.Present(delivery);
        var blocks = new List<object>
        {
            new { type = "header", text = new { type = "plain_text", text = WebhookChannelAdapterHelpers.Limit($"{WebhookChannelAdapterHelpers.SeverityEmoji(card.Severity)} {card.Title}", 150), emoji = true } }
        };
        if (!string.IsNullOrWhiteSpace(card.Summary))
            blocks.Add(new { type = "section", text = new { type = "mrkdwn", text = WebhookChannelAdapterHelpers.EscapeSlack(card.Summary!) } });
        if (card.Fields.Count > 0)
            blocks.Add(new
            {
                type = "section",
                fields = card.Fields.Take(10).Select(field => new
                {
                    type = "mrkdwn",
                    text = $"*{WebhookChannelAdapterHelpers.EscapeSlack(field.Label)}*\n{WebhookChannelAdapterHelpers.EscapeSlack(field.Value)}"
                }).ToArray()
            });
        blocks.Add(new { type = "context", elements = new[] { new { type = "mrkdwn", text = WebhookChannelAdapterHelpers.EscapeSlack(card.Footer) } } });
        return WebhookChannelAdapterHelpers.JsonPost(destination, new
        {
            text = WebhookChannelAdapterHelpers.Limit(message, 35000),
            blocks
        });
    }
}

internal sealed class DiscordWebhookChannelAdapter : IWebhookChannelAdapter
{
    public string Id => "discord";
    public WebhookProviderDescriptor Descriptor { get; } = new("discord", "Discord",
        "发送到 Discord Webhook，使用 Embed 原生卡片展示指标，并禁用自动 mentions。",
        "https://discord.com/api/webhooks/...", "无需填写", "Discord 使用 Webhook URL 作为凭据。");

    public HttpRequestMessage CreateRequest(Uri destination, WebhookDeliveryRequested delivery, string envelopeJson, string message, string? secret)
    {
        var card = WebhookChannelMessageFormatter.Present(delivery);
        return WebhookChannelAdapterHelpers.JsonPost(destination, new
        {
            allowed_mentions = new { parse = Array.Empty<string>() },
            embeds = new[]
            {
                new
                {
                    title = WebhookChannelAdapterHelpers.Limit($"{WebhookChannelAdapterHelpers.SeverityEmoji(card.Severity)} {card.Title}", 256),
                    description = string.IsNullOrWhiteSpace(card.Summary) ? "\u200B" : WebhookChannelAdapterHelpers.Limit(card.Summary!, 2048),
                    color = WebhookChannelAdapterHelpers.DiscordColor(card.Severity),
                    fields = card.Fields.Take(25).Select(field => new
                    {
                        name = WebhookChannelAdapterHelpers.Limit(field.Label, 256),
                        value = WebhookChannelAdapterHelpers.Limit(field.Value, 1024),
                        inline = field.Short
                    }).ToArray(),
                    footer = new { text = WebhookChannelAdapterHelpers.Limit(card.Footer, 2048) },
                    timestamp = delivery.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
                }
            }
        });
    }
}

internal sealed class TeamsWebhookChannelAdapter : IWebhookChannelAdapter
{
    public string Id => "teams";
    public WebhookProviderDescriptor Descriptor { get; } = new("teams", "Microsoft Teams",
        "发送到 Teams Workflows / Incoming Webhook，使用 Adaptive Card 原生布局展示事件与指标。",
        "https://...webhook.office.com/...", "无需填写", "Teams Workflow URL 通常已包含所需授权信息。");

    public HttpRequestMessage CreateRequest(Uri destination, WebhookDeliveryRequested delivery, string envelopeJson, string message, string? secret)
    {
        var card = WebhookChannelMessageFormatter.Present(delivery);
        var body = new List<object>
        {
            new { type = "TextBlock", text = $"{WebhookChannelAdapterHelpers.SeverityEmoji(card.Severity)} {card.Title}", weight = "Bolder", size = "Medium", wrap = true }
        };
        if (!string.IsNullOrWhiteSpace(card.Summary))
            body.Add(new { type = "TextBlock", text = card.Summary, wrap = true, spacing = "Small" });
        if (card.Fields.Count > 0)
            body.Add(new
            {
                type = "FactSet",
                facts = card.Fields.Select(field => new { title = field.Label + ":", value = field.Value }).ToArray()
            });
        body.Add(new { type = "TextBlock", text = card.Footer, wrap = true, isSubtle = true, size = "Small", spacing = "Medium" });

        return WebhookChannelAdapterHelpers.JsonPost(destination, new
        {
            type = "message",
            attachments = new[] { new {
                contentType = "application/vnd.microsoft.card.adaptive",
                contentUrl = (string?)null,
                content = new {
                    type = "AdaptiveCard", version = "1.4", body = body.ToArray()
                }
            }}
        });
    }
}

internal sealed class FeishuWebhookChannelAdapter : IWebhookChannelAdapter
{
    public string Id => "feishu";
    public WebhookProviderDescriptor Descriptor { get; } = new("feishu", "飞书 / Lark",
        "发送飞书/Lark 自定义机器人原生消息卡片；配置签名密钥时自动生成 timestamp 与 sign。",
        "https://open.feishu.cn/open-apis/bot/v2/hook/...", "机器人签名密钥（可选）", "机器人开启签名校验时填写安全设置中的签名密钥。");

    public HttpRequestMessage CreateRequest(Uri destination, WebhookDeliveryRequested delivery, string envelopeJson, string message, string? secret)
    {
        var card = WebhookChannelMessageFormatter.Present(delivery);
        var elements = new List<object>();
        if (!string.IsNullOrWhiteSpace(card.Summary))
            elements.Add(new
            {
                tag = "div",
                text = new { tag = "lark_md", content = WebhookChannelAdapterHelpers.EscapeAngleMarkup(card.Summary!) }
            });
        if (card.Fields.Count > 0)
        {
            elements.Add(new
            {
                tag = "div",
                fields = card.Fields.Select(field => new
                {
                    is_short = field.Short,
                    text = new { tag = "lark_md", content = $"**{WebhookChannelAdapterHelpers.EscapeAngleMarkup(field.Label)}：**\n{WebhookChannelAdapterHelpers.EscapeAngleMarkup(field.Value)}" }
                }).ToArray()
            });
        }
        elements.Add(new { tag = "hr" });
        elements.Add(new
        {
            tag = "note",
            elements = new[] { new { tag = "plain_text", content = WebhookChannelAdapterHelpers.Limit(card.Footer, 500) } }
        });

        var payload = new Dictionary<string, object?>
        {
            ["msg_type"] = "interactive",
            ["card"] = new
            {
                config = new { wide_screen_mode = true },
                header = new
                {
                    template = WebhookChannelAdapterHelpers.FeishuTemplate(card.Severity),
                    title = new { tag = "plain_text", content = $"{WebhookChannelAdapterHelpers.SeverityEmoji(card.Severity)} {card.Title}" }
                },
                elements = elements.ToArray()
            }
        };
        if (!string.IsNullOrWhiteSpace(secret))
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            payload["timestamp"] = timestamp;
            payload["sign"] = WebhookChannelAdapterHelpers.SignFeishu(timestamp, secret);
        }
        return WebhookChannelAdapterHelpers.JsonPost(destination, payload);
    }
}

internal sealed class DingTalkWebhookChannelAdapter : IWebhookChannelAdapter
{
    public string Id => "dingtalk";
    public WebhookProviderDescriptor Descriptor { get; } = new("dingtalk", "钉钉",
        "发送到钉钉自定义机器人，使用 Markdown 原生消息布局；配置加签密钥时自动附加 timestamp 与 sign。",
        "https://oapi.dingtalk.com/robot/send?access_token=...", "机器人加签密钥（可选）", "机器人启用加签时填写安全设置中的密钥。");

    public HttpRequestMessage CreateRequest(Uri destination, WebhookDeliveryRequested delivery, string envelopeJson, string message, string? secret)
    {
        var target = destination;
        if (!string.IsNullOrWhiteSpace(secret))
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
            var sign = WebhookChannelAdapterHelpers.SignDingTalk(timestamp, secret);
            var builder = new UriBuilder(destination);
            var existingQuery = builder.Query.TrimStart('?');
            var signatureQuery = $"timestamp={Uri.EscapeDataString(timestamp)}&sign={Uri.EscapeDataString(sign)}";
            builder.Query = string.IsNullOrEmpty(existingQuery) ? signatureQuery : $"{existingQuery}&{signatureQuery}";
            target = builder.Uri;
        }

        var card = WebhookChannelMessageFormatter.Present(delivery);
        var text = new StringBuilder()
            .Append("### ").Append(WebhookChannelAdapterHelpers.SeverityEmoji(card.Severity)).Append(' ').AppendLine(card.Title)
            .AppendLine();
        if (!string.IsNullOrWhiteSpace(card.Summary)) text.AppendLine(WebhookChannelAdapterHelpers.EscapeAngleMarkup(card.Summary!)).AppendLine();
        foreach (var field in card.Fields)
            text.Append("- **").Append(WebhookChannelAdapterHelpers.EscapeAngleMarkup(field.Label)).Append("：** ")
                .AppendLine(WebhookChannelAdapterHelpers.EscapeAngleMarkup(field.Value));
        text.AppendLine().Append("---\n").Append(WebhookChannelAdapterHelpers.EscapeAngleMarkup(card.Footer));

        return WebhookChannelAdapterHelpers.JsonPost(target, new
        {
            msgtype = "markdown",
            markdown = new { title = WebhookChannelAdapterHelpers.Limit(card.Title, 64), text = WebhookChannelAdapterHelpers.Limit(text.ToString(), 3500) }
        });
    }
}

internal sealed class WeComWebhookChannelAdapter : IWebhookChannelAdapter
{
    public string Id => "wecom";
    public WebhookProviderDescriptor Descriptor { get; } = new("wecom", "企业微信群机器人",
        "发送到企业微信群机器人，使用 Markdown 原生通知样式；Webhook URL 中的 key 用于鉴权。",
        "https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=...", "无需填写", "企业微信使用 URL 中的 key 作为凭据。");

    public HttpRequestMessage CreateRequest(Uri destination, WebhookDeliveryRequested delivery, string envelopeJson, string message, string? secret)
    {
        var card = WebhookChannelMessageFormatter.Present(delivery);
        var titleColor = card.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase) || card.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)
            ? "warning"
            : "info";
        var text = new StringBuilder()
            .Append("<font color=\"").Append(titleColor).Append("\">**")
            .Append(WebhookChannelAdapterHelpers.SeverityEmoji(card.Severity)).Append(' ')
            .Append(WebhookChannelAdapterHelpers.EscapeAngleMarkup(card.Title)).AppendLine("**</font>");
        if (!string.IsNullOrWhiteSpace(card.Summary)) text.AppendLine(WebhookChannelAdapterHelpers.EscapeAngleMarkup(card.Summary!));
        foreach (var field in card.Fields)
            text.Append('>').Append(" **").Append(WebhookChannelAdapterHelpers.EscapeAngleMarkup(field.Label)).Append("：** ")
                .AppendLine(WebhookChannelAdapterHelpers.EscapeAngleMarkup(field.Value));
        text.AppendLine().Append("> ").Append(WebhookChannelAdapterHelpers.EscapeAngleMarkup(card.Footer));

        return WebhookChannelAdapterHelpers.JsonPost(destination, new
        {
            msgtype = "markdown",
            markdown = new { content = WebhookChannelAdapterHelpers.Limit(text.ToString(), 4000) }
        });
    }
}