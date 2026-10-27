using System.Security.Claims;

namespace KnowledgeRag.Api.Security;

public static class HttpActorContext
{
    public static ActorContext From(ClaimsPrincipal principal)
    {
        var userId = GetUserId(principal);
        Guid? apiKeyId = Guid.TryParse(principal.FindFirstValue("kr:api_key_id"), out var key) ? key : null;
        Guid? tenantId = Guid.TryParse(principal.FindFirstValue("kr:tenant_id"), out var tenant) ? tenant : null;
        Guid? knowledgeBaseId = Guid.TryParse(principal.FindFirstValue("kr:knowledge_base_id"), out var kb) ? kb : null;
        var scopes = principal.FindAll("kr:scope").Select(x => x.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new ActorContext(userId, apiKeyId, tenantId, knowledgeBaseId, scopes);
    }

    public static Guid GetUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(value, out var userId))
            throw new UnauthorizedAccessException("无效的登录用户。");
        return userId;
    }
}
