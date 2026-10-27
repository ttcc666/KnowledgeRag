namespace KnowledgeRag.Domain.Entities;

public sealed class ConversationEntity
{
    public Guid Id { get; set; }
    public Guid KnowledgeBaseId { get; set; }
    public Guid UserId { get; set; }
    public Guid? ApiKeyId { get; set; }
    public string Title { get; set; } = "新会话";
    public string? AgentSessionJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ChatMessageEntity
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? SourcesJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
