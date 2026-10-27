using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.AI;

public sealed class OpenAiCompatibleEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;

    public OpenAiCompatibleEmbeddingService(HttpClient httpClient, IOptions<AiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
    }

    public async Task<float[]> GenerateAsync(string input, CancellationToken cancellationToken = default)
        => (await GenerateBatchAsync([input], cancellationToken))[0];

    public async Task<IReadOnlyList<float[]>> GenerateBatchAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0) return [];

        var payload = new Dictionary<string, object?>
        {
            ["model"] = _options.EmbeddingModel,
            ["input"] = inputs
        };

        if (_options.SendEmbeddingDimensions && _options.EmbeddingDimensions > 0)
        {
            payload["dimensions"] = _options.EmbeddingDimensions;
        }

        using var response = await _httpClient.PostAsJsonAsync("embeddings", payload, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Embedding API failed ({(int)response.StatusCode}): {raw}");
        }

        using var doc = JsonDocument.Parse(raw);
        var data = doc.RootElement.GetProperty("data")
            .EnumerateArray()
            .OrderBy(x => x.TryGetProperty("index", out var index) ? index.GetInt32() : 0)
            .ToList();

        var result = new List<float[]>(data.Count);
        foreach (var item in data)
        {
            var vector = item.GetProperty("embedding").EnumerateArray().Select(x => x.GetSingle()).ToArray();
            if (_options.EmbeddingDimensions > 0 && vector.Length != _options.EmbeddingDimensions)
            {
                throw new InvalidOperationException(
                    $"Embedding dimension mismatch. Configured {_options.EmbeddingDimensions}, API returned {vector.Length}. " +
                    "Update AI:EmbeddingDimensions and recreate the Qdrant collection.");
            }
            result.Add(vector);
        }

        if (result.Count != inputs.Count)
        {
            throw new InvalidOperationException($"Embedding API returned {result.Count} vectors for {inputs.Count} inputs.");
        }

        return result;
    }
}
