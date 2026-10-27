namespace KnowledgeRag.Infrastructure.Configuration;

public sealed class RerankerOptions
{
    public const string SectionName = "Reranker";

    /// <summary>none | local | llm</summary>
    public string Mode { get; set; } = "local";
    public string? Model { get; set; }
    public int CandidateCount { get; set; } = 16;
    public int MaxDocumentCharacters { get; set; } = 1600;
}
