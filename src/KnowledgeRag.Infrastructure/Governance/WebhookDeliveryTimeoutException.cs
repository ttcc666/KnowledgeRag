using System.Net;

namespace KnowledgeRag.Infrastructure.Governance;

public sealed class WebhookDeliveryTimeoutException : HttpRequestException
{
    public WebhookDeliveryTimeoutException(TimeSpan timeout, Exception innerException)
        : base(BuildMessage(timeout), innerException, HttpStatusCode.RequestTimeout)
    {
        Timeout = timeout;
    }

    public TimeSpan Timeout { get; }

    private static string BuildMessage(TimeSpan timeout)
    {
        var seconds = timeout.TotalSeconds;
        var display = double.IsFinite(seconds) && seconds > 0
            ? $"{seconds:0.#} 秒"
            : "配置的超时时间";
        return $"Webhook 请求在 {display}内未收到响应，已按可重试失败处理。";
    }
}
