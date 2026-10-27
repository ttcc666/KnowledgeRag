using System.Diagnostics;

namespace KnowledgeRag.Infrastructure.Observability;

public static class Telemetry
{
    public const string ActivitySourceName = "KnowledgeRag.NonAzure";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
