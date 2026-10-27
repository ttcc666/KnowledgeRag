namespace KnowledgeRag.Domain.Entities;

public enum KnowledgeBaseRole
{
    Viewer = 1,
    Editor = 2,
    Owner = 3
}

public sealed class KnowledgeBaseEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class KnowledgeBaseMemberEntity
{
    public Guid KnowledgeBaseId { get; set; }
    public Guid UserId { get; set; }
    public KnowledgeBaseRole Role { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}
