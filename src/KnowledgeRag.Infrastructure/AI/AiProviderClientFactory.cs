using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http.Headers;
using KnowledgeRag.Infrastructure.Governance;
using OpenAI;
using OpenAI.Chat;

namespace KnowledgeRag.Infrastructure.AI;

public sealed record ResolvedAiEndpoint(
    Guid? DeploymentId,
    Guid? ConnectionId,
    string ProviderType,
    string BaseUrl,
    string ApiKey,
    string Model,
    int Revision,
    bool IsTenantManaged);

public sealed class AiProviderClientFactory(IHttpClientFactory httpClientFactory)
{
    public ChatClient CreateChatClient(ResolvedAiEndpoint endpoint, string? modelOverride = null)
    {
        var baseUri = ResolveBaseUri(endpoint);
        var credential = new ApiKeyCredential(string.IsNullOrWhiteSpace(endpoint.ApiKey) ? "local-development-key" : endpoint.ApiKey);
        var options = new OpenAIClientOptions { Endpoint = baseUri };
        if (endpoint.IsTenantManaged)
        {
            var httpClient = httpClientFactory.CreateClient("tenant-ai-provider");
            options.Transport = new HttpClientPipelineTransport(httpClient);
        }
        return new ChatClient(string.IsNullOrWhiteSpace(modelOverride) ? endpoint.Model : modelOverride, credential, options);
    }

    public HttpRequestMessage CreateJsonRequest(ResolvedAiEndpoint endpoint, HttpMethod method, string relativePath)
    {
        var baseUri = ResolveBaseUri(endpoint);
        var request = new HttpRequestMessage(method, new Uri(baseUri, relativePath.TrimStart('/')));
        if (!string.IsNullOrWhiteSpace(endpoint.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
        return request;
    }

    public HttpClient CreateHttpClient(ResolvedAiEndpoint endpoint)
    {
        _ = ResolveBaseUri(endpoint);
        return endpoint.IsTenantManaged
            ? httpClientFactory.CreateClient("tenant-ai-provider")
            : httpClientFactory.CreateClient("platform-ai-provider");
    }

    private static Uri ResolveBaseUri(ResolvedAiEndpoint endpoint)
    {
        if (!endpoint.ProviderType.Equals(AiProviderTypes.OpenAiCompatible, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"不支持的 AI Provider：{endpoint.ProviderType}。");

        if (!Uri.TryCreate(endpoint.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var uri))
            throw new InvalidOperationException("AI Provider Base URL 无效。");

        if (!endpoint.IsTenantManaged) return uri;
        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("租户管理的 AI Provider Base URL 必须使用 HTTPS。");
        return WebhookUrlPolicy.ValidateUrl(uri.ToString());
    }
}
