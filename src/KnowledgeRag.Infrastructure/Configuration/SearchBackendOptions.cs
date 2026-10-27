namespace KnowledgeRag.Infrastructure.Configuration;

public sealed class SearchBackendOptions
{
    public const string SectionName = "SearchBackend";

    /// <summary>qdrant | pgvector</summary>
    public string Vector { get; set; } = "qdrant";

    /// <summary>bm25 | postgres_fts</summary>
    public string Lexical { get; set; } = "bm25";

    public string PostgresTextSearchConfig { get; set; } = "simple";
    public bool AutoInitializeNativeIndexes { get; set; } = true;
}
