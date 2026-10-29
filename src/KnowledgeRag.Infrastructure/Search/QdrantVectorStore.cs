using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<Guid, int> _initializedCollections = new();

    public QdrantVectorStore(HttpClient httpClient, IOptions<QdrantOptions> qdrant, IOptions<AiOptions> ai)
    {
        _httpClient = httpClient;
        _qdrant = qdrant.Value;
        _ai = ai.Value;
        _httpClient.BaseAddress = new Uri(_qdrant.BaseUrl.TrimEnd('/') + "/");
        if (!string.IsNullOrWhiteSpace(_qdrant.ApiKey))
            _httpClient.DefaultRequestHeaders.Add("api-key", _qdrant.ApiKey);
    }

    public Task EnsureCollectionAsync(CancellationToken cancellationToken = default)
        => EnsureCollectionAsync(Guid.Empty, Math.Clamp(_ai.EmbeddingDimensions, 1, 16000), cancellationToken);

    public async Task EnsureCollectionAsync(Guid indexGenerationId, int dimensions, CancellationToken cancellationToken = default)
    {
        if (dimensions is < 1 or > 16000) throw new InvalidOperationException("Qdrant embedding dimensions are invalid.");
        if (_initializedCollections.TryGetValue(indexGenerationId, out var cachedDimensions))
        {
            if (cachedDimensions != dimensions)
                throw new InvalidOperationException($"Index generation expects {cachedDimensions} embedding dimensions, received {dimensions}.");
            return;
        }

        await _initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (_initializedCollections.TryGetValue(indexGenerationId, out cachedDimensions))
            {
                if (cachedDimensions != dimensions)
                    throw new InvalidOperationException($"Index generation expects {cachedDimensions} embedding dimensions, received {dimensions}.");
                return;
            }

            var collection = CollectionName(indexGenerationId);
            using var exists = await _httpClient.GetAsync($"collections/{Uri.EscapeDataString(collection)}", cancellationToken);
            if (exists.IsSuccessStatusCode)
            {
                var existingRaw = await exists.Content.ReadAsStringAsync(cancellationToken);
                if (TryGetVectorSize(existingRaw, out var existingDimensions) && existingDimensions != dimensions)
                    throw new InvalidOperationException($"Qdrant collection '{collection}' has {existingDimensions} dimensions; expected {dimensions}.");
                if (!HasPayloadIndex(existingRaw, "knowledge_base_id"))
                    await CreateKeywordPayloadIndexAsync(collection, "knowledge_base_id", cancellationToken);
                _initializedCollections[indexGenerationId] = dimensions;
                return;
            }
            if ((int)exists.StatusCode != 404)
                throw new InvalidOperationException($"Qdrant collection check failed: {await exists.Content.ReadAsStringAsync(cancellationToken)}");

            var body = new { vectors = new { size = dimensions, distance = "Cosine" } };
            using var create = await _httpClient.PutAsJsonAsync($"collections/{Uri.EscapeDataString(collection)}", body, cancellationToken);
            var raw = await create.Content.ReadAsStringAsync(cancellationToken);
            if (!create.IsSuccessStatusCode) throw new InvalidOperationException($"Qdrant collection creation failed: {raw}");
            await CreateKeywordPayloadIndexAsync(collection, "knowledge_base_id", cancellationToken);
            _initializedCollections[indexGenerationId] = dimensions;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public async Task UpsertAsync(IReadOnlyList<VectorPoint> points, CancellationToken cancellationToken = default)
    {
        if (points.Count == 0) return;
        var generationId = points[0].IndexGenerationId;
        var dimensions = points[0].Vector.Length;
        if (dimensions is < 1 or > 16000 || points.Any(x => x.IndexGenerationId != generationId || x.Vector.Length != dimensions))
            throw new InvalidOperationException("A Qdrant upsert batch must contain one index generation and one vector dimension.");
        await EnsureCollectionAsync(generationId, dimensions, cancellationToken);
        var collection = CollectionName(generationId);
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
                    index_generation_id = p.IndexGenerationId.ToString(),
                    file_name = p.FileName,
                    chunk_index = p.ChunkIndex
                }
            })
        };
        using var response = await _httpClient.PutAsJsonAsync(
            $"collections/{Uri.EscapeDataString(collection)}/points?wait=true", body, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Qdrant upsert failed: {raw}");
    }

    public Task<IReadOnlyList<VectorCandidate>> SearchAsync(
        float[] vector, Guid knowledgeBaseId, int topK, CancellationToken cancellationToken = default)
        => SearchAsync(vector, knowledgeBaseId, Guid.Empty, topK, cancellationToken);

    public async Task<IReadOnlyList<VectorCandidate>> SearchAsync(
        float[] vector,
        Guid knowledgeBaseId,
        Guid indexGenerationId,
        int topK,
        CancellationToken cancellationToken = default)
    {
        await EnsureCollectionAsync(indexGenerationId, vector.Length, cancellationToken);
        var collection = CollectionName(indexGenerationId);
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
            limit = Math.Clamp(topK, 1, 1000),
            with_payload = false,
            with_vector = false
        };

        using var response = await _httpClient.PostAsJsonAsync(
            $"collections/{Uri.EscapeDataString(collection)}/points/query", body, cancellationToken);
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

    public Task DeleteAsync(IReadOnlyList<Guid> pointIds, CancellationToken cancellationToken = default)
        => DeleteAsync(Guid.Empty, pointIds, cancellationToken);

    public async Task DeleteAsync(Guid indexGenerationId, IReadOnlyList<Guid> pointIds, CancellationToken cancellationToken = default)
    {
        if (pointIds.Count == 0) return;
        var collection = CollectionName(indexGenerationId);
        if (indexGenerationId == Guid.Empty)
            await EnsureCollectionAsync(Guid.Empty, Math.Clamp(_ai.EmbeddingDimensions, 1, 16000), cancellationToken);
        else if (!await CollectionExistsAsync(collection, cancellationToken))
            return;

        foreach (var batch in pointIds.Chunk(256))
        {
            var body = new { points = batch.Select(x => x.ToString()).ToArray() };
            using var response = await _httpClient.PostAsJsonAsync(
                $"collections/{Uri.EscapeDataString(collection)}/points/delete?wait=true", body, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode && (int)response.StatusCode != 404)
                throw new InvalidOperationException($"Qdrant delete failed: {raw}");
        }
    }

    public async Task DeleteGenerationAsync(Guid knowledgeBaseId, Guid indexGenerationId, CancellationToken cancellationToken = default)
    {
        var collection = CollectionName(indexGenerationId);
        if (indexGenerationId != Guid.Empty)
        {
            using var response = await _httpClient.DeleteAsync($"collections/{Uri.EscapeDataString(collection)}", cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode && (int)response.StatusCode != 404)
                throw new InvalidOperationException($"Qdrant generation cleanup failed: {raw}");
            _initializedCollections.TryRemove(indexGenerationId, out _);
            return;
        }

        await EnsureCollectionAsync(Guid.Empty, Math.Clamp(_ai.EmbeddingDimensions, 1, 16000), cancellationToken);
        var body = new
        {
            filter = new
            {
                must = new object[]
                {
                    new { key = "knowledge_base_id", match = new { value = knowledgeBaseId.ToString() } }
                }
            }
        };
        using var delete = await _httpClient.PostAsJsonAsync(
            $"collections/{Uri.EscapeDataString(collection)}/points/delete?wait=true", body, cancellationToken);
        var deleteRaw = await delete.Content.ReadAsStringAsync(cancellationToken);
        if (!delete.IsSuccessStatusCode && (int)delete.StatusCode != 404)
            throw new InvalidOperationException($"Qdrant legacy-generation cleanup failed: {deleteRaw}");
    }

    public Task EnsureReadyAsync(CancellationToken cancellationToken = default) => EnsureCollectionAsync(cancellationToken);

    private async Task<bool> CollectionExistsAsync(string collection, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync($"collections/{Uri.EscapeDataString(collection)}", ct);
        if (response.IsSuccessStatusCode) return true;
        if ((int)response.StatusCode == 404) return false;
        throw new InvalidOperationException($"Qdrant collection check failed: {await response.Content.ReadAsStringAsync(ct)}");
    }

    private async Task CreateKeywordPayloadIndexAsync(string collection, string fieldName, CancellationToken cancellationToken)
    {
        var body = new { field_name = fieldName, field_schema = "keyword" };
        using var response = await _httpClient.PutAsJsonAsync(
            $"collections/{Uri.EscapeDataString(collection)}/index?wait=true", body, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Qdrant payload index creation failed for '{fieldName}': {raw}");
    }

    private string CollectionName(Guid indexGenerationId)
        => indexGenerationId == Guid.Empty ? _qdrant.Collection : $"{_qdrant.Collection}-{indexGenerationId:N}";

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

    private static bool TryGetVectorSize(string collectionJson, out int dimensions)
    {
        dimensions = 0;
        try
        {
            using var doc = JsonDocument.Parse(collectionJson);
            if (!doc.RootElement.TryGetProperty("result", out var result) ||
                !result.TryGetProperty("config", out var config) ||
                !config.TryGetProperty("params", out var parameters) ||
                !parameters.TryGetProperty("vectors", out var vectors) ||
                !vectors.TryGetProperty("size", out var size) ||
                !size.TryGetInt32(out dimensions))
                return false;
            return true;
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
