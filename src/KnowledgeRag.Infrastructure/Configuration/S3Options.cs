namespace KnowledgeRag.Infrastructure.Configuration;

public sealed class S3Options
{
    public const string SectionName = "S3";

    public string Endpoint { get; set; } = "http://localhost:9000";
    public string Region { get; set; } = "us-east-1";
    public string Bucket { get; set; } = "knowledge-rag";
    public string AccessKey { get; set; } = "minioadmin";
    public string SecretKey { get; set; } = "minioadmin";
    public bool ForcePathStyle { get; set; } = true;
}
