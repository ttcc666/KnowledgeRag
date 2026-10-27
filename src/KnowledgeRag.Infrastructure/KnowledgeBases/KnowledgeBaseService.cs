using KnowledgeRag.Infrastructure.Persistence;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Application.Security;
using KnowledgeRag.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeRag.Infrastructure.KnowledgeBases;

public sealed class KnowledgeBaseService(
    AppDbContext db,
    KnowledgeBaseAccessService access,
    TenantAccessService tenantAccess,
    IVectorStore vectorStore,
    IFileStorage fileStorage)
{
    public async Task<KnowledgeBaseEntity> CreateAsync(ActorContext actor, CreateKnowledgeBaseRequest request, CancellationToken ct = default)
    {
        Validate(request.Name, request.Description);
        var tenantId = request.TenantId ?? await ResolveDefaultTenantAsync(actor, ct);
        if (actor.IsApiKey)
        {
            if (actor.TenantId != tenantId || actor.KnowledgeBaseId.HasValue || !actor.HasScope(ApiKeyScopes.KnowledgeAdmin))
                throw new UnauthorizedAccessException("API Key 没有在该租户创建知识库的权限。");
        }
        else
        {
            await tenantAccess.RequireAsync(actor, tenantId, TenantRole.Member, ct);
        }

        var now = DateTimeOffset.UtcNow;
        var kb = new KnowledgeBaseEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = request.Name.Trim(), Description = request.Description?.Trim(),
            CreatedByUserId = actor.UserId, CreatedAt = now, UpdatedAt = now
        };
        db.KnowledgeBases.Add(kb);
        db.KnowledgeBaseMembers.Add(new KnowledgeBaseMemberEntity { KnowledgeBaseId = kb.Id, UserId = actor.UserId, Role = KnowledgeBaseRole.Owner, AddedAt = now });
        await db.SaveChangesAsync(ct);
        return kb;
    }

    public async Task<IReadOnlyList<KnowledgeBaseSummary>> ListAsync(ActorContext actor, CancellationToken ct = default)
    {
        if (actor.IsApiKey)
        {
            if (!actor.TenantId.HasValue) return [];
            var query = db.KnowledgeBases.AsNoTracking().Where(x => x.TenantId == actor.TenantId.Value);
            if (actor.KnowledgeBaseId.HasValue) query = query.Where(x => x.Id == actor.KnowledgeBaseId.Value);
            var role = actor.HasScope(ApiKeyScopes.KnowledgeAdmin) ? KnowledgeBaseRole.Owner : actor.HasScope(ApiKeyScopes.KnowledgeWrite) ? KnowledgeBaseRole.Editor : KnowledgeBaseRole.Viewer;
            var rows = await query.Select(x => new KnowledgeBaseSummary(x.Id, x.TenantId, x.Name, x.Description, role, x.CreatedAt, x.UpdatedAt)).ToListAsync(ct);
            return rows.OrderByDescending(x => x.UpdatedAt).ToList();
        }

        var memberRows = await (from kb in db.KnowledgeBases.AsNoTracking()
                                join member in db.KnowledgeBaseMembers.AsNoTracking() on kb.Id equals member.KnowledgeBaseId
                                where member.UserId == actor.UserId
                                select new KnowledgeBaseSummary(kb.Id, kb.TenantId, kb.Name, kb.Description, member.Role, kb.CreatedAt, kb.UpdatedAt))
            .ToListAsync(ct);
        return memberRows.OrderByDescending(x => x.UpdatedAt).ToList();
    }

    public async Task<KnowledgeBaseEntity?> GetAsync(ActorContext actor, Guid knowledgeBaseId, CancellationToken ct = default)
    {
        await access.RequireAsync(actor, knowledgeBaseId, KnowledgeBaseRole.Viewer, ct);
        return await db.KnowledgeBases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == knowledgeBaseId, ct);
    }

    public async Task UpdateAsync(ActorContext actor, Guid knowledgeBaseId, UpdateKnowledgeBaseRequest request, CancellationToken ct = default)
    {
        await access.RequireAsync(actor, knowledgeBaseId, KnowledgeBaseRole.Editor, ct);
        Validate(request.Name, request.Description);
        var kb = await db.KnowledgeBases.SingleAsync(x => x.Id == knowledgeBaseId, ct);
        kb.Name = request.Name.Trim(); kb.Description = request.Description?.Trim(); kb.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<KnowledgeBaseMemberSummary>> ListMembersAsync(ActorContext actor, Guid knowledgeBaseId, CancellationToken ct = default)
    {
        await access.RequireAsync(actor, knowledgeBaseId, KnowledgeBaseRole.Owner, ct);
        return await (from member in db.KnowledgeBaseMembers.AsNoTracking()
                      join user in db.Users.AsNoTracking() on member.UserId equals user.Id
                      where member.KnowledgeBaseId == knowledgeBaseId
                      orderby member.Role descending, user.DisplayName
                      select new KnowledgeBaseMemberSummary(user.Id, user.Email, user.DisplayName, member.Role, member.AddedAt)).ToListAsync(ct);
    }

    public async Task AddOrUpdateMemberAsync(ActorContext actor, Guid knowledgeBaseId, AddKnowledgeBaseMemberRequest request, CancellationToken ct = default)
    {
        await access.RequireAsync(actor, knowledgeBaseId, KnowledgeBaseRole.Owner, ct);
        if (!Enum.IsDefined(request.Role)) throw new InvalidOperationException("无效的知识库角色。");
        var user = await db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == (request.Email ?? string.Empty).Trim().ToUpperInvariant(), ct)
            ?? throw new InvalidOperationException("目标用户不存在，请让对方先注册账号。");
        var tenantId = await db.KnowledgeBases.Where(x => x.Id == knowledgeBaseId).Select(x => x.TenantId).SingleAsync(ct);
        if (!await db.TenantMembers.AnyAsync(x => x.TenantId == tenantId && x.UserId == user.Id, ct))
            db.TenantMembers.Add(new TenantMemberEntity { TenantId = tenantId, UserId = user.Id, Role = TenantRole.Member, AddedAt = DateTimeOffset.UtcNow });
        var member = await db.KnowledgeBaseMembers.SingleOrDefaultAsync(x => x.KnowledgeBaseId == knowledgeBaseId && x.UserId == user.Id, ct);
        if (member is null)
            db.KnowledgeBaseMembers.Add(new KnowledgeBaseMemberEntity { KnowledgeBaseId = knowledgeBaseId, UserId = user.Id, Role = request.Role, AddedAt = DateTimeOffset.UtcNow });
        else
        {
            if (member.Role == KnowledgeBaseRole.Owner && request.Role != KnowledgeBaseRole.Owner && await OwnerCountAsync(knowledgeBaseId, ct) <= 1)
                throw new InvalidOperationException("知识库至少需要保留一个 Owner。");
            member.Role = request.Role;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveMemberAsync(ActorContext actor, Guid knowledgeBaseId, Guid memberUserId, CancellationToken ct = default)
    {
        await access.RequireAsync(actor, knowledgeBaseId, KnowledgeBaseRole.Owner, ct);
        var member = await db.KnowledgeBaseMembers.SingleOrDefaultAsync(x => x.KnowledgeBaseId == knowledgeBaseId && x.UserId == memberUserId, ct);
        if (member is null) return;
        if (member.Role == KnowledgeBaseRole.Owner && await OwnerCountAsync(knowledgeBaseId, ct) <= 1) throw new InvalidOperationException("不能删除最后一个 Owner。");
        db.KnowledgeBaseMembers.Remove(member);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(ActorContext actor, Guid knowledgeBaseId, CancellationToken ct = default)
    {
        await access.RequireAsync(actor, knowledgeBaseId, KnowledgeBaseRole.Owner, ct);
        var kb = await db.KnowledgeBases.SingleOrDefaultAsync(x => x.Id == knowledgeBaseId, ct);
        if (kb is null) return;
        var points = await db.DocumentChunks.AsNoTracking().Where(x => x.KnowledgeBaseId == knowledgeBaseId).Select(x => x.Id).ToListAsync(ct);
        var files = await db.Documents.AsNoTracking().Where(x => x.KnowledgeBaseId == knowledgeBaseId).Select(x => x.StoredFileName).ToListAsync(ct);
        await vectorStore.DeleteAsync(points, ct);
        foreach (var file in files) await fileStorage.DeleteAsync(knowledgeBaseId, file, ct);
        db.KnowledgeBases.Remove(kb);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Guid> ResolveDefaultTenantAsync(ActorContext actor, CancellationToken ct)
    {
        if (actor.IsApiKey && actor.TenantId.HasValue) return actor.TenantId.Value;
        var id = await db.TenantMembers.AsNoTracking()
            .Where(x => x.UserId == actor.UserId)
            .OrderByDescending(x => x.Role)
            .Select(x => x.TenantId)
            .FirstOrDefaultAsync(ct);
        if (id == Guid.Empty) throw new InvalidOperationException("当前用户还没有租户。");
        return id;
    }

    private Task<int> OwnerCountAsync(Guid kbId, CancellationToken ct) => db.KnowledgeBaseMembers.CountAsync(x => x.KnowledgeBaseId == kbId && x.Role == KnowledgeBaseRole.Owner, ct);
    private static void Validate(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200) throw new InvalidOperationException("知识库名称不能为空且不能超过 200 个字符。");
        if ((description?.Length ?? 0) > 2000) throw new InvalidOperationException("知识库描述不能超过 2000 个字符。");
    }
}
