namespace KnowledgeRag.Application.Security;

public sealed record ActorContext(
    Guid UserId,
    Guid? ApiKeyId,
    Guid? TenantId,
    Guid? KnowledgeBaseId,
    IReadOnlySet<string> Scopes)
{
    public bool IsApiKey => ApiKeyId.HasValue;

    public bool HasScope(string scope)
        => Scopes.Contains(scope) || Scopes.Contains("*");
}

public static class ApiKeyScopes
{
    public const string KnowledgeRead = "knowledge:read";
    public const string KnowledgeWrite = "knowledge:write";
    public const string KnowledgeAdmin = "knowledge:admin";
    public const string TenantAdmin = "tenant:admin";
    public static readonly string[] Allowed = [KnowledgeRead, KnowledgeWrite, KnowledgeAdmin, TenantAdmin];
}
