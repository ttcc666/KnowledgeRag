namespace KnowledgeRag.Infrastructure.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string Provider { get; set; } = "Sqlite";
    public string ConnectionString { get; set; } = "Data Source=App_Data/knowledge.db";
    public bool AutoMigrate { get; set; } = true;
}
