using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KnowledgeRag.Infrastructure.Documents;

public static class DocumentIndexVersion
{
    /// <summary>
    /// Produces a stable point id for a document/index-generation/chunk tuple. The generation is
    /// the persisted queue timestamp captured before processing begins. Stable ids make a delivery
    /// retry idempotent without adding a schema column solely for staging state.
    /// </summary>
    public static Guid CreatePointId(Guid documentId, long generationTicks, int chunkIndex)
    {
        if (generationTicks <= 0) throw new ArgumentOutOfRangeException(nameof(generationTicks));
        if (chunkIndex < 0) throw new ArgumentOutOfRangeException(nameof(chunkIndex));

        Span<byte> input = stackalloc byte[28];
        documentId.TryWriteBytes(input[..16]);
        BinaryPrimitives.WriteInt64LittleEndian(input[16..24], generationTicks);
        BinaryPrimitives.WriteInt32LittleEndian(input[24..28], chunkIndex);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        Span<byte> guidBytes = stackalloc byte[16];
        hash[..16].CopyTo(guidBytes);
        return new Guid(guidBytes);
    }

    /// <summary>Produces a stable point id scoped to a KB-wide embedding generation.</summary>
    public static Guid CreatePointId(Guid documentId, Guid indexGenerationId, int chunkIndex)
    {
        if (indexGenerationId == Guid.Empty) throw new ArgumentException("A non-empty index generation id is required.", nameof(indexGenerationId));
        if (chunkIndex < 0) throw new ArgumentOutOfRangeException(nameof(chunkIndex));

        var input = System.Text.Encoding.UTF8.GetBytes($"kb-index-v1:{documentId:N}:{indexGenerationId:N}:{chunkIndex}");
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
    }
}
