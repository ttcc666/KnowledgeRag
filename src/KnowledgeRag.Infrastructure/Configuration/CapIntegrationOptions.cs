namespace KnowledgeRag.Infrastructure.Configuration;

public sealed class CapIntegrationOptions
{
    public const string SectionName = "Cap";

    /// <summary>inmemory | rabbitmq</summary>
    public string Transport { get; set; } = "inmemory";
    public int FailedRetryCount { get; set; } = 20;
    public RabbitMqIntegrationOptions RabbitMq { get; set; } = new();
}

public sealed class RabbitMqIntegrationOptions
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string ExchangeName { get; set; } = "knowledge-rag.cap";
}
