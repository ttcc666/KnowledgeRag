namespace KnowledgeRag.Infrastructure.Configuration;

public sealed class QdrantOptions
{
    public const string SectionName = "Qdrant";

    public string BaseUrl { get; set; } = "http://localhost:6333";
    public string? ApiKey { get; set; }
    public string Collection { get; set; } = "knowledge_chunks";
}
