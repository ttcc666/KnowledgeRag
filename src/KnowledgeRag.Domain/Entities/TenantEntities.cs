namespace KnowledgeRag.Domain.Entities;

public enum TenantRole
{
    Member = 1,
    Admin = 2,
    Owner = 3
}

public sealed class TenantEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TenantMemberEntity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public TenantRole Role { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}
