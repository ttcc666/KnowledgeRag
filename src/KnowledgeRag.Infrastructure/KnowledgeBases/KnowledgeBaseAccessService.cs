using KnowledgeRag.Infrastructure.Persistence;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Application.Security;
using KnowledgeRag.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeRag.Infrastructure.KnowledgeBases;

public sealed class KnowledgeBaseAccessService(AppDbContext db)
{
    public async Task<KnowledgeBaseRole?> GetRoleAsync(Guid userId, Guid knowledgeBaseId, CancellationToken cancellationToken = default)
        => await db.KnowledgeBaseMembers.AsNoTracking()
            .Where(x => x.UserId == userId && x.KnowledgeBaseId == knowledgeBaseId)
            .Select(x => (KnowledgeBaseRole?)x.Role).SingleOrDefaultAsync(cancellationToken);

    public async Task RequireAsync(ActorContext actor, Guid knowledgeBaseId, KnowledgeBaseRole minimumRole, CancellationToken cancellationToken = default)
    {
        if (actor.IsApiKey)
        {
            var tenantId = await db.KnowledgeBases.AsNoTracking().Where(x => x.Id == knowledgeBaseId).Select(x => (Guid?)x.TenantId).SingleOrDefaultAsync(cancellationToken);
            if (!tenantId.HasValue) throw new KeyNotFoundException("知识库不存在。");
            if (actor.TenantId != tenantId || (actor.KnowledgeBaseId.HasValue && actor.KnowledgeBaseId != knowledgeBaseId))
                throw new UnauthorizedAccessException("该 API Key 无权访问此知识库。");
            var allowed = minimumRole switch
            {
                KnowledgeBaseRole.Viewer => actor.HasScope(ApiKeyScopes.KnowledgeRead) || actor.HasScope(ApiKeyScopes.KnowledgeWrite) || actor.HasScope(ApiKeyScopes.KnowledgeAdmin),
                KnowledgeBaseRole.Editor => actor.HasScope(ApiKeyScopes.KnowledgeWrite) || actor.HasScope(ApiKeyScopes.KnowledgeAdmin),
                KnowledgeBaseRole.Owner => actor.HasScope(ApiKeyScopes.KnowledgeAdmin),
                _ => false
            };
            if (!allowed) throw new UnauthorizedAccessException("该 API Key 缺少所需 scope。");
            return;
        }

        var role = await GetRoleAsync(actor.UserId, knowledgeBaseId, cancellationToken);
        if (role is null || role.Value < minimumRole) throw new UnauthorizedAccessException("你没有访问该知识库所需的权限。");
    }
}
