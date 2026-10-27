using System.Text.RegularExpressions;
using KnowledgeRag.Infrastructure.Persistence;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Application.Security;
using KnowledgeRag.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeRag.Infrastructure.Tenancy;

public sealed class TenantService(AppDbContext db, TenantAccessService access, IVectorStore vectorStore, IFileStorage fileStorage)
{
    public async Task<IReadOnlyList<TenantSummary>> ListAsync(ActorContext actor, CancellationToken ct = default)
    {
        if (actor.IsApiKey && actor.TenantId.HasValue)
        {
            var role = actor.HasScope(ApiKeyScopes.TenantAdmin) ? TenantRole.Admin : TenantRole.Member;
            return await db.Tenants.AsNoTracking().Where(x => x.Id == actor.TenantId.Value)
                .Select(x => new TenantSummary(x.Id, x.Name, x.Slug, role, x.CreatedAt, x.UpdatedAt)).ToListAsync(ct);
        }
        return await (from tenant in db.Tenants.AsNoTracking()
                      join member in db.TenantMembers.AsNoTracking() on tenant.Id equals member.TenantId
                      where member.UserId == actor.UserId
                      orderby tenant.UpdatedAt descending
                      select new TenantSummary(tenant.Id, tenant.Name, tenant.Slug, member.Role, tenant.CreatedAt, tenant.UpdatedAt)).ToListAsync(ct);
    }

    public async Task<TenantEntity> CreateAsync(ActorContext actor, CreateTenantRequest request, CancellationToken ct = default)
    {
        if (actor.IsApiKey) throw new UnauthorizedAccessException("API Key 不能创建租户。");
        var name = ValidateName(request.Name);
        var now = DateTimeOffset.UtcNow;
        var tenant = new TenantEntity
        {
            Id = Guid.NewGuid(), Name = name, Slug = await UniqueSlugAsync(name, ct), CreatedByUserId = actor.UserId, CreatedAt = now, UpdatedAt = now
        };
        db.Tenants.Add(tenant);
        db.TenantMembers.Add(new TenantMemberEntity { TenantId = tenant.Id, UserId = actor.UserId, Role = TenantRole.Owner, AddedAt = now });
        await db.SaveChangesAsync(ct);
        return tenant;
    }

    public async Task UpdateAsync(ActorContext actor, Guid tenantId, UpdateTenantRequest request, CancellationToken ct = default)
    {
        await access.RequireAsync(actor, tenantId, TenantRole.Admin, ct);
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId, ct) ?? throw new KeyNotFoundException("租户不存在。");
        tenant.Name = ValidateName(request.Name);
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TenantMemberSummary>> ListMembersAsync(ActorContext actor, Guid tenantId, CancellationToken ct = default)
    {
        await access.RequireAsync(actor, tenantId, TenantRole.Admin, ct);
        return await (from member in db.TenantMembers.AsNoTracking()
                      join user in db.Users.AsNoTracking() on member.UserId equals user.Id
                      where member.TenantId == tenantId
                      orderby member.Role descending, user.DisplayName
                      select new TenantMemberSummary(user.Id, user.Email, user.DisplayName, member.Role, member.AddedAt)).ToListAsync(ct);
    }

    public async Task AddOrUpdateMemberAsync(ActorContext actor, Guid tenantId, AddTenantMemberRequest request, CancellationToken ct = default)
    {
        await access.RequireAsync(actor, tenantId, TenantRole.Admin, ct);
        if (!Enum.IsDefined(request.Role)) throw new InvalidOperationException("无效的租户角色。");
        var normalized = (request.Email ?? string.Empty).Trim().ToUpperInvariant();
        var user = await db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalized, ct)
            ?? throw new InvalidOperationException("目标用户不存在，请让对方先注册账号。");
        var member = await db.TenantMembers.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == user.Id, ct);
        if (member is null)
            db.TenantMembers.Add(new TenantMemberEntity { TenantId = tenantId, UserId = user.Id, Role = request.Role, AddedAt = DateTimeOffset.UtcNow });
        else
        {
            if (member.Role == TenantRole.Owner && request.Role != TenantRole.Owner && await OwnerCountAsync(tenantId, ct) <= 1)
                throw new InvalidOperationException("租户至少需要保留一个 Owner。");
            member.Role = request.Role;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveMemberAsync(ActorContext actor, Guid tenantId, Guid memberUserId, CancellationToken ct = default)
    {
        await access.RequireAsync(actor, tenantId, TenantRole.Admin, ct);
        var member = await db.TenantMembers.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == memberUserId, ct);
        if (member is null) return;
        if (member.Role == TenantRole.Owner && await OwnerCountAsync(tenantId, ct) <= 1)
            throw new InvalidOperationException("不能删除最后一个 Owner。");

        var ownedKnowledgeBases = await (
            from membership in db.KnowledgeBaseMembers.AsNoTracking()
            join kb in db.KnowledgeBases.AsNoTracking() on membership.KnowledgeBaseId equals kb.Id
            where kb.TenantId == tenantId && membership.UserId == memberUserId && membership.Role == KnowledgeBaseRole.Owner
            select new { kb.Id, kb.Name }).ToListAsync(ct);
        var lastOwnedKnowledgeBases = new List<string>();
        foreach (var kb in ownedKnowledgeBases)
        {
            if (await db.KnowledgeBaseMembers.CountAsync(x => x.KnowledgeBaseId == kb.Id && x.Role == KnowledgeBaseRole.Owner, ct) <= 1)
                lastOwnedKnowledgeBases.Add(kb.Name);
        }
        if (lastOwnedKnowledgeBases.Count > 0)
            throw new InvalidOperationException($"该成员仍是以下知识库的唯一 Owner，请先转移所有权：{string.Join(", ", lastOwnedKnowledgeBases)}");

        var knowledgeBaseIds = await db.KnowledgeBases.AsNoTracking().Where(x => x.TenantId == tenantId).Select(x => x.Id).ToListAsync(ct);
        var knowledgeMemberships = await db.KnowledgeBaseMembers
            .Where(x => x.UserId == memberUserId && knowledgeBaseIds.Contains(x.KnowledgeBaseId)).ToListAsync(ct);
        db.KnowledgeBaseMembers.RemoveRange(knowledgeMemberships);
        db.TenantMembers.Remove(member);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(ActorContext actor, Guid tenantId, CancellationToken ct = default)
    {
        if (actor.IsApiKey) throw new UnauthorizedAccessException("API Key 不能删除租户。");
        await access.RequireAsync(actor, tenantId, TenantRole.Owner, ct);
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId, ct);
        if (tenant is null) return;
        var kbIds = await db.KnowledgeBases.AsNoTracking().Where(x => x.TenantId == tenantId).Select(x => x.Id).ToListAsync(ct);
        var pointIds = await db.DocumentChunks.AsNoTracking().Where(x => kbIds.Contains(x.KnowledgeBaseId)).Select(x => x.Id).ToListAsync(ct);
        var files = await db.Documents.AsNoTracking().Where(x => kbIds.Contains(x.KnowledgeBaseId)).Select(x => new { x.KnowledgeBaseId, x.StoredFileName }).ToListAsync(ct);
        await vectorStore.DeleteAsync(pointIds, ct);
        foreach (var file in files) await fileStorage.DeleteAsync(file.KnowledgeBaseId, file.StoredFileName, ct);
        db.Tenants.Remove(tenant);
        await db.SaveChangesAsync(ct);
    }

    private Task<int> OwnerCountAsync(Guid tenantId, CancellationToken ct)
        => db.TenantMembers.CountAsync(x => x.TenantId == tenantId && x.Role == TenantRole.Owner, ct);

    private async Task<string> UniqueSlugAsync(string value, CancellationToken ct)
    {
        var baseSlug = Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9\u4e00-\u9fff]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(baseSlug)) baseSlug = "tenant";
        if (baseSlug.Length > 80) baseSlug = baseSlug[..80];
        var candidate = baseSlug;
        while (await db.Tenants.AnyAsync(x => x.Slug == candidate, ct)) candidate = $"{baseSlug}-{Guid.NewGuid():N}"[..Math.Min(100, baseSlug.Length + 9)];
        return candidate;
    }

    private static string ValidateName(string name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length is < 1 or > 200) throw new InvalidOperationException("租户名称长度必须在 1-200 个字符之间。");
        return value;
    }
}
