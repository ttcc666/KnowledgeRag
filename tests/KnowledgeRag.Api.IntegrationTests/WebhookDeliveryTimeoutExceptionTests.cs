using System.Net;
using KnowledgeRag.Infrastructure.Governance;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class WebhookDeliveryTimeoutExceptionTests
{
    [Fact]
    public void Timeout_IsClassifiedAsRetryableHttpRequestFailure()
    {
        var inner = new TaskCanceledException("A task was canceled.");
        var exception = new WebhookDeliveryTimeoutException(TimeSpan.FromSeconds(8), inner);

        Assert.Equal(HttpStatusCode.RequestTimeout, exception.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(8), exception.Timeout);
        Assert.Same(inner, exception.InnerException);
        Assert.Contains("8 秒", exception.Message, StringComparison.Ordinal);
        Assert.Contains("可重试失败", exception.Message, StringComparison.Ordinal);
    }
}
