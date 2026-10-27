namespace KnowledgeRag.Infrastructure.Configuration;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>local | s3</summary>
    public string Provider { get; set; } = "local";
    public string RootPath { get; set; } = "App_Data";
}
