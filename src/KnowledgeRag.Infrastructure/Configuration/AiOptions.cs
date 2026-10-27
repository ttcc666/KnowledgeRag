namespace KnowledgeRag.Infrastructure.Configuration;

public sealed class AiOptions
{
    public const string SectionName = "AI";

    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = string.Empty;
    public string ChatModel { get; set; } = "gpt-4o-mini";
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public int EmbeddingDimensions { get; set; } = 1536;
    public bool SendEmbeddingDimensions { get; set; } = false;
}
