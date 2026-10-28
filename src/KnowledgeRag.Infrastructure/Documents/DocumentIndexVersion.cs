using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KnowledgeRag.Infrastructure.Documents;

public static class DocumentIndexVersion
{
    public static int Next(int currentVersion)
    {
        if (currentVersion < 0) throw new ArgumentOutOfRangeException(nameof(currentVersion));
        return checked(currentVersion + 1);
    }

    /// <summary>
    /// Produces a stable point id for a document/index generation/chunk tuple. Stable ids make
    /// retries idempotent: a retried build overwrites any orphaned staging points from the same
    /// generation instead of creating an unbounded set of unreachable vectors.
    /// </summary>
    public static Guid CreatePointId(Guid documentId, int indexVersion, int chunkIndex)
    {
        if (indexVersion <= 0) throw new ArgumentOutOfRangeException(nameof(indexVersion));
        if (chunkIndex < 0) throw new ArgumentOutOfRangeException(nameof(chunkIndex));

        Span<byte> input = stackalloc byte[24];
        documentId.TryWriteBytes(input[..16]);
        BinaryPrimitives.WriteInt32LittleEndian(input[16..20], indexVersion);
        BinaryPrimitives.WriteInt32LittleEndian(input[20..24], chunkIndex);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        Span<byte> guidBytes = stackalloc byte[16];
        hash[..16].CopyTo(guidBytes);
        return new Guid(guidBytes);
    }
}
