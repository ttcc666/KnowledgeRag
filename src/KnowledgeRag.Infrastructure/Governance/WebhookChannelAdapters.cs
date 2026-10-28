using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KnowledgeRag.Infrastructure.Governance;

public sealed record WebhookProviderDescriptor(string Id, string Name, string Description, string UrlPlaceholder, string SecretLabel, string SecretHint);

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

        var message = delivery.EventType.ToLowerInvariant() switch
        {
            "webhook.test" => FormatTest(delivery, payload),
            "rag.run.completed" => FormatRagRunCompleted(delivery, payload),
            "evaluation.completed" => FormatEvaluationCompleted(delivery, payload),
            _ => FormatGeneric(delivery, payload)
        };
        return (envelopeJson, message);
    }

    private static string FormatTest(WebhookDeliveryRequested delivery, JsonElement payload)
    {
        var configuredMessage = ReadString(payload, "message");
        var lines = new List<string>
        {
            "✅ KnowledgeRag 通知测试",
            string.IsNullOrWhiteSpace(configuredMessage) ? "消息通道配置已生效，可以正常接收 KnowledgeRag 通知。" : configuredMessage,
            $"时间：{FormatTime(delivery.CreatedAt)}",
            $"Delivery ID：{ShortId(delivery.EventId)}"
        };
        return string.Join("\n", lines);
    }

    private static string FormatRagRunCompleted(WebhookDeliveryRequested delivery, JsonElement payload)
    {
        var status = ReadString(payload, "status") ?? "completed";
        var runId = ReadString(payload, "id");
        var sourceCount = ReadInt64(payload, "sourceCount");
        var latencyMs = ReadDouble(payload, "latencyMs");
        var inputTokens = ReadInt64(payload, "estimatedInputTokens");
        var outputTokens = ReadInt64(payload, "estimatedOutputTokens");
        var cost = ReadDecimal(payload, "estimatedCostUsd");
        var model = ReadString(payload, "answerModel");
        var agentMode = ReadString(payload, "agentMode");

        var lines = new List<string>
        {
            status.Equals("completed", StringComparison.OrdinalIgnoreCase)
                ? "✅ KnowledgeRag · RAG 运行完成"
                : $"ℹ️ KnowledgeRag · RAG 运行 {status}",
            $"状态：{DisplayStatus(status)}"
        };
        if (sourceCount.HasValue) lines.Add($"引用来源：{sourceCount.Value:N0} 个");
        if (latencyMs.HasValue) lines.Add($"耗时：{FormatDuration(latencyMs.Value)}");
        if (inputTokens.HasValue || outputTokens.HasValue)
            lines.Add($"Token：{FormatCount(inputTokens)} 输入 / {FormatCount(outputTokens)} 输出");
        if (cost.HasValue) lines.Add($"估算成本：${cost.Value.ToString("0.######", CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(model)) lines.Add($"回答模型：{model}");
        if (!string.IsNullOrWhiteSpace(agentMode)) lines.Add($"Agent 模式：{agentMode}");
        if (!string.IsNullOrWhiteSpace(runId)) lines.Add($"Run ID：{ShortId(runId)}");
        lines.Add($"时间：{FormatTime(delivery.CreatedAt)}");
        return string.Join("\n", lines);
    }

    private static string FormatEvaluationCompleted(WebhookDeliveryRequested delivery, JsonElement payload)
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
        var lines = new List<string>
        {
            hasRegression ? "⚠️ KnowledgeRag · Evaluation 完成（检测到回归）" : "✅ KnowledgeRag · Evaluation 完成"
        };
        if (passRate.HasValue) lines.Add($"通过率：{FormatPercent(passRate.Value)}");
        if (groundednessRate.HasValue) lines.Add($"Groundedness：{FormatPercent(groundednessRate.Value)}");
        if (citationValidityRate.HasValue) lines.Add($"Citation validity：{FormatPercent(citationValidityRate.Value)}");
        if (noAnswerAccuracy.HasValue) lines.Add($"No-answer accuracy：{FormatPercent(noAnswerAccuracy.Value)}");
        if (latencyMs.HasValue) lines.Add($"平均耗时：{FormatDuration(latencyMs.Value)}");
        if (hasRegression)
        {
            var regressions = new List<string>();
            if (passRateRegression == true) regressions.Add("通过率");
            if (groundednessRegression == true) regressions.Add("Groundedness");
            if (latencyRegression == true) regressions.Add("延迟");
            lines.Add($"回归项：{string.Join("、", regressions)}");
        }
        if (!string.IsNullOrWhiteSpace(runId)) lines.Add($"Run ID：{ShortId(runId)}");
        lines.Add($"时间：{FormatTime(delivery.CreatedAt)}");
        return string.Join("\n", lines);
    }

    private static string FormatGeneric(WebhookDeliveryRequested delivery, JsonElement payload)
    {
        var lines = new List<string>
        {
            $"🔔 KnowledgeRag · {delivery.EventType}",
            $"时间：{FormatTime(delivery.CreatedAt)}"
        };

        if (payload.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in payload.EnumerateObject())
            {
                if (lines.Count >= 10) break;
                var value = FormatPrimitive(property.Value);
                if (!string.IsNullOrWhiteSpace(value)) lines.Add($"{Humanize(property.Name)}：{value}");
            }
        }
        return string.Join("\n", lines);
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
        => value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

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
        "发送到 Slack Incoming Webhook；URL 本身是凭据，无需额外 Token。",
        "https://hooks.slack.com/services/...", "无需填写", "Slack 使用 Incoming Webhook URL 作为凭据。");
    public HttpRequestMessage CreateRequest(Uri destination, WebhookDeliveryRequested delivery, string envelopeJson, string message, string? secret)
        => WebhookChannelAdapterHelpers.JsonPost(destination, new { text = WebhookChannelAdapterHelpers.Limit(message, 35000) });
}

internal sealed class DiscordWebhookChannelAdapter : IWebhookChannelAdapter
{
    public string Id => "discord";
    public WebhookProviderDescriptor Descriptor { get; } = new("discord", "Discord",
        "发送到 Discord Webhook；自动限制消息长度。",
        "https://discord.com/api/webhooks/...", "无需填写", "Discord 使用 Webhook URL 作为凭据。");
    public HttpRequestMessage CreateRequest(Uri destination, WebhookDeliveryRequested delivery, string envelopeJson, string message, string? secret)
        => WebhookChannelAdapterHelpers.JsonPost(destination, new { content = WebhookChannelAdapterHelpers.Limit(message, 1900) });
}

internal sealed class TeamsWebhookChannelAdapter : IWebhookChannelAdapter
{
    public string Id => "teams";
    public WebhookProviderDescriptor Descriptor { get; } = new("teams", "Microsoft Teams",
        "发送到 Teams Workflows / Incoming Webhook，使用 Adaptive Card 展示事件与数据。",
        "https://...webhook.office.com/...", "无需填写", "Teams Workflow URL 通常已包含所需授权信息。");
    public HttpRequestMessage CreateRequest(Uri destination, WebhookDeliveryRequested delivery, string envelopeJson, string message, string? secret)
        => WebhookChannelAdapterHelpers.JsonPost(destination, new
        {
            type = "message",
            attachments = new[] { new {
                contentType = "application/vnd.microsoft.card.adaptive",
                content = new {
                    type = "AdaptiveCard", version = "1.4",
                    body = new object[] {
                        new { type = "TextBlock", text = $"KnowledgeRag · {delivery.EventType}", weight = "Bolder", size = "Medium", wrap = true },
                        new { type = "TextBlock", text = WebhookChannelAdapterHelpers.Limit(message, 20000), wrap = true }
                    }
                }
            }}
        });
}

internal sealed class FeishuWebhookChannelAdapter : IWebhookChannelAdapter
{
    public string Id => "feishu";
    public WebhookProviderDescriptor Descriptor { get; } = new("feishu", "飞书 / Lark",
        "发送飞书/Lark 自定义机器人文本消息；配置签名密钥时自动生成 timestamp 与 sign。",
        "https://open.feishu.cn/open-apis/bot/v2/hook/...", "机器人签名密钥（可选）", "机器人开启签名校验时填写安全设置中的签名密钥。");
    public HttpRequestMessage CreateRequest(Uri destination, WebhookDeliveryRequested delivery, string envelopeJson, string message, string? secret)
    {
        var payload = new Dictionary<string, object?>
        {
            ["msg_type"] = "text",
            ["content"] = new { text = WebhookChannelAdapterHelpers.Limit(message, 4000) }
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
        "发送到钉钉自定义机器人；配置加签密钥时自动附加 timestamp 与 sign。",
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
        return WebhookChannelAdapterHelpers.JsonPost(target, new {
            msgtype = "text", text = new { content = WebhookChannelAdapterHelpers.Limit(message, 1800) }
        });
    }
}

internal sealed class WeComWebhookChannelAdapter : IWebhookChannelAdapter
{
    public string Id => "wecom";
    public WebhookProviderDescriptor Descriptor { get; } = new("wecom", "企业微信群机器人",
        "发送到企业微信群机器人；Webhook URL 中的 key 用于鉴权。",
        "https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=...", "无需填写", "企业微信使用 URL 中的 key 作为凭据。");
    public HttpRequestMessage CreateRequest(Uri destination, WebhookDeliveryRequested delivery, string envelopeJson, string message, string? secret)
        => WebhookChannelAdapterHelpers.JsonPost(destination, new {
            msgtype = "text", text = new { content = WebhookChannelAdapterHelpers.Limit(message, 1800) }
        });
}
