using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Application.Security;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.Search;

public sealed class QdrantVectorStore : IVectorStore
{
    private readonly HttpClient _httpClient;
    private readonly QdrantOptions _qdrant;
    private readonly AiOptions _ai;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private bool _initialized;

    public QdrantVectorStore(HttpClient httpClient, IOptions<QdrantOptions> qdrant, IOptions<AiOptions> ai)
    {
        _httpClient = httpClient;
        _qdrant = qdrant.Value;
        _ai = ai.Value;
        _httpClient.BaseAddress = new Uri(_qdrant.BaseUrl.TrimEnd('/') + "/");
        if (!string.IsNullOrWhiteSpace(_qdrant.ApiKey))
            _httpClient.DefaultRequestHeaders.Add("api-key", _qdrant.ApiKey);
    }

    public async Task EnsureCollectionAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;
        await _initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            using var exists = await _httpClient.GetAsync($"collections/{Uri.EscapeDataString(_qdrant.Collection)}", cancellationToken);
            if (exists.IsSuccessStatusCode)
            {
                var existingRaw = await exists.Content.ReadAsStringAsync(cancellationToken);
                if (!HasPayloadIndex(existingRaw, "knowledge_base_id"))
                    await CreateKeywordPayloadIndexAsync("knowledge_base_id", cancellationToken);
                _initialized = true;
                return;
            }
            if ((int)exists.StatusCode != 404)
                throw new InvalidOperationException($"Qdrant collection check failed: {await exists.Content.ReadAsStringAsync(cancellationToken)}");

            var body = new { vectors = new { size = _ai.EmbeddingDimensions, distance = "Cosine" } };
            using var create = await _httpClient.PutAsJsonAsync($"collections/{Uri.EscapeDataString(_qdrant.Collection)}", body, cancellationToken);
            var raw = await create.Content.ReadAsStringAsync(cancellationToken);
            if (!create.IsSuccessStatusCode) throw new InvalidOperationException($"Qdrant collection creation failed: {raw}");
            await CreateKeywordPayloadIndexAsync("knowledge_base_id", cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public async Task UpsertAsync(IReadOnlyList<VectorPoint> points, CancellationToken cancellationToken = default)
    {
        if (points.Count == 0) return;
        await EnsureCollectionAsync(cancellationToken);
        var body = new
        {
            points = points.Select(p => new
            {
                id = p.Id,
                vector = p.Vector,
                payload = new
                {
                    knowledge_base_id = p.KnowledgeBaseId.ToString(),
                    document_id = p.DocumentId.ToString(),
                    file_name = p.FileName,
                    chunk_index = p.ChunkIndex
                }
            })
        };
        using var response = await _httpClient.PutAsJsonAsync(
            $"collections/{Uri.EscapeDataString(_qdrant.Collection)}/points?wait=true", body, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Qdrant upsert failed: {raw}");
    }

    public async Task<IReadOnlyList<VectorCandidate>> SearchAsync(
        float[] vector,
        Guid knowledgeBaseId,
        int topK,
        CancellationToken cancellationToken = default)
    {
        await EnsureCollectionAsync(cancellationToken);
        var body = new
        {
            query = vector,
            filter = new
            {
                must = new object[]
                {
                    new { key = "knowledge_base_id", match = new { value = knowledgeBaseId.ToString() } }
                }
            },
            limit = topK,
            with_payload = false,
            with_vector = false
        };

        using var response = await _httpClient.PostAsJsonAsync(
            $"collections/{Uri.EscapeDataString(_qdrant.Collection)}/points/query", body, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Qdrant search failed: {raw}");

        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("points", out var points) ||
            points.ValueKind != JsonValueKind.Array)
            return [];

        var hits = new List<VectorCandidate>();
        foreach (var item in points.EnumerateArray())
        {
            if (!TryParseGuid(item.GetProperty("id"), out var id)) continue;
            hits.Add(new VectorCandidate(id, item.GetProperty("score").GetDouble()));
        }
        return hits;
    }

    public async Task DeleteAsync(IReadOnlyList<Guid> pointIds, CancellationToken cancellationToken = default)
    {
        if (pointIds.Count == 0) return;
        await EnsureCollectionAsync(cancellationToken);
        foreach (var batch in pointIds.Chunk(256))
        {
            var body = new { points = batch.Select(x => x.ToString()).ToArray() };
            using var response = await _httpClient.PostAsJsonAsync(
                $"collections/{Uri.EscapeDataString(_qdrant.Collection)}/points/delete?wait=true", body, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Qdrant delete failed: {raw}");
        }
    }


    public Task EnsureReadyAsync(CancellationToken cancellationToken = default) => EnsureCollectionAsync(cancellationToken);

    private async Task CreateKeywordPayloadIndexAsync(string fieldName, CancellationToken cancellationToken)
    {
        var body = new { field_name = fieldName, field_schema = "keyword" };
        using var response = await _httpClient.PutAsJsonAsync(
            $"collections/{Uri.EscapeDataString(_qdrant.Collection)}/index?wait=true", body, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Qdrant payload index creation failed for '{fieldName}': {raw}");
    }

    private static bool HasPayloadIndex(string collectionJson, string fieldName)
    {
        try
        {
            using var doc = JsonDocument.Parse(collectionJson);
            return doc.RootElement.TryGetProperty("result", out var result) &&
                   result.TryGetProperty("payload_schema", out var schema) &&
                   schema.ValueKind == JsonValueKind.Object &&
                   schema.TryGetProperty(fieldName, out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseGuid(JsonElement element, out Guid value)
    {
        if (element.ValueKind == JsonValueKind.String) return Guid.TryParse(element.GetString(), out value);
        value = default;
        return false;
    }
}
