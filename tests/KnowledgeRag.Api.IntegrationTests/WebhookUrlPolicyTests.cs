using System.Net;
using KnowledgeRag.Infrastructure.Governance;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class WebhookUrlPolicyTests
{
    [Theory]
    [InlineData("http://localhost/hook")]
    [InlineData("http://127.0.0.1/hook")]
    [InlineData("http://10.10.0.2/hook")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://192.168.1.5/hook")]
    [InlineData("http://[::1]/hook")]
    [InlineData("http://[fc00::1]/hook")]
    public void ValidateUrl_BlocksLocalAndPrivateDestinations(string url)
        => Assert.ThrowsAny<Exception>(() => WebhookUrlPolicy.ValidateUrl(url));

    [Fact]
    public void ValidateUrl_AcceptsPublicHttpsDestination()
    {
        var uri = WebhookUrlPolicy.ValidateUrl("https://example.com/webhooks/knowledge-rag");
        Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
    }

    [Fact]
    public void CreateHttpHandler_DisablesAmbientProxy_AndKeepsSsrfConnectionCallback()
    {
        using var handler = WebhookSafeConnection.CreateHttpHandler();

        Assert.False(handler.UseProxy);
        Assert.False(handler.AllowAutoRedirect);
        Assert.NotNull(handler.ConnectCallback);
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("1.1.1.1", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("192.168.0.1", false)]
    [InlineData("169.254.1.1", false)]
    public void IsPublicAddress_ClassifiesIpv4Ranges(string address, bool expected)
        => Assert.Equal(expected, WebhookUrlPolicy.IsPublicAddress(IPAddress.Parse(address)));
}
