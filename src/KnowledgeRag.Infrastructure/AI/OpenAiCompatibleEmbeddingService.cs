using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.AI;

public sealed class OpenAiCompatibleEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly AiProviderClientFactory? _clientFactory;

    public OpenAiCompatibleEmbeddingService(
        HttpClient httpClient,
        IOptions<AiOptions> options,
        AiProviderClientFactory? clientFactory = null)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _clientFactory = clientFactory;

        _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Authorization = null;
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    public async Task<float[]> GenerateAsync(string input, CancellationToken cancellationToken = default)
        => (await GenerateBatchAsync([input], cancellationToken))[0];

    public async Task<float[]> GenerateAsync(string input, EmbeddingEndpointResolution endpoint, CancellationToken cancellationToken = default)
        => (await GenerateBatchAsync([input], endpoint, cancellationToken))[0];

    public Task<IReadOnlyList<float[]>> GenerateBatchAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
        => GenerateBatchAsync(inputs, new EmbeddingEndpointResolution(
            new ResolvedAiEndpoint(null, null, AiProviderTypes.OpenAiCompatible, _options.BaseUrl, _options.ApiKey,
                _options.EmbeddingModel, 0, false),
            _options.EmbeddingDimensions,
            _options.SendEmbeddingDimensions,
            "Platform default",
            null), cancellationToken);

    public async Task<IReadOnlyList<float[]>> GenerateBatchAsync(
        IReadOnlyList<string> inputs,
        EmbeddingEndpointResolution endpoint,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0) return [];
        if (endpoint.Dimensions <= 0) throw new InvalidOperationException("Embedding dimensions must be greater than zero.");

        var payload = new Dictionary<string, object?>
        {
            ["model"] = endpoint.Endpoint.Model,
            ["input"] = inputs
        };
        if (endpoint.SendDimensions) payload["dimensions"] = endpoint.Dimensions;

        HttpClient? ownedClient = null;
        HttpRequestMessage request;
        HttpClient client;
        if (_clientFactory is not null)
        {
            ownedClient = _clientFactory.CreateHttpClient(endpoint.Endpoint);
            client = ownedClient;
            request = _clientFactory.CreateJsonRequest(endpoint.Endpoint, HttpMethod.Post, "embeddings");
        }
        else
        {
            if (endpoint.Endpoint.IsTenantManaged ||
                !endpoint.Endpoint.BaseUrl.TrimEnd('/').Equals(_options.BaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A provider-aware HTTP client is required for this embedding endpoint.");
            client = _httpClient;
            request = new HttpRequestMessage(HttpMethod.Post, "embeddings");
            request.Headers.Authorization = null;
            if (!string.IsNullOrWhiteSpace(endpoint.Endpoint.ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.Endpoint.ApiKey);
        }

        try
        {
            using (request)
            {
                request.Content = JsonContent.Create(payload);
                using var response = await client.SendAsync(request, cancellationToken);
                var raw = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Embedding API failed ({(int)response.StatusCode}): {raw}");

                using var doc = JsonDocument.Parse(raw);
                var data = doc.RootElement.GetProperty("data")
                    .EnumerateArray()
                    .OrderBy(x => x.TryGetProperty("index", out var index) ? index.GetInt32() : 0)
                    .ToList();
                var result = new List<float[]>(data.Count);
                foreach (var item in data)
                {
                    var vector = item.GetProperty("embedding").EnumerateArray().Select(x => x.GetSingle()).ToArray();
                    if (vector.Length != endpoint.Dimensions)
                        throw new InvalidOperationException(
                            $"Embedding dimension mismatch for model '{endpoint.Endpoint.Model}'. Configured {endpoint.Dimensions}, API returned {vector.Length}.");
                    result.Add(vector);
                }
                if (result.Count != inputs.Count)
                    throw new InvalidOperationException($"Embedding API returned {result.Count} vectors for {inputs.Count} inputs.");
                return result;
            }
        }
        finally
        {
            ownedClient?.Dispose();
        }
    }
}
