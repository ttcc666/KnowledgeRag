namespace KnowledgeRag.Domain.Entities;

public enum DocumentStatus
{
    Queued = 0,
    Processing = 1,
    Ready = 2,
    Failed = 3,
    Cancelled = 4
}

public sealed class DocumentEntity
{
    public Guid Id { get; set; }
    public Guid KnowledgeBaseId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int ChunkCount { get; set; }
    /// <summary>
    /// Monotonically increasing active index generation. Zero means the document has never
    /// completed indexing. Re-indexing builds the next generation before atomically swapping it in.
    /// </summary>
    public int IndexVersion { get; set; }
    public DocumentStatus Status { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class DocumentChunkEntity
{
    /// <summary>Also used as the vector-store point UUID.</summary>
    public Guid Id { get; set; }
    public Guid KnowledgeBaseId { get; set; }
    public Guid DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Text { get; set; } = string.Empty;
    public int CharacterCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
