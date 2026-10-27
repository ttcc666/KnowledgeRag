using KnowledgeRag.Infrastructure.Persistence;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Application.Security;
using KnowledgeRag.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeRag.Infrastructure.Tenancy;

public sealed class TenantAccessService(AppDbContext db)
{
    public async Task<TenantRole?> GetRoleAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
        => await db.TenantMembers.AsNoTracking()
            .Where(x => x.UserId == userId && x.TenantId == tenantId)
            .Select(x => (TenantRole?)x.Role)
            .SingleOrDefaultAsync(ct);

    public async Task RequireAsync(ActorContext actor, Guid tenantId, TenantRole minimumRole, CancellationToken ct = default)
    {
        if (actor.IsApiKey)
        {
            if (actor.TenantId != tenantId || actor.KnowledgeBaseId.HasValue || !actor.HasScope(ApiKeyScopes.TenantAdmin))
                throw new UnauthorizedAccessException("该 API Key 没有租户管理权限。");
            return;
        }

        var role = await GetRoleAsync(actor.UserId, tenantId, ct);
        if (role is null || role.Value < minimumRole)
            throw new UnauthorizedAccessException("你没有访问该租户所需的权限。");
    }
}
