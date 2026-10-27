using System.Security.Cryptography;
using System.Text;
using KnowledgeRag.Infrastructure.Persistence;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Application.Security;
using KnowledgeRag.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeRag.Infrastructure.Identity;

public sealed class ApiKeyService(AppDbContext db, TenantAccessService access)
{
    public async Task<IReadOnlyList<ApiKeySummary>> ListAsync(ActorContext actor, Guid tenantId, CancellationToken ct = default)
    {
        if (actor.IsApiKey) throw new UnauthorizedAccessException("API Key 不能管理其他 API Key。");
        await access.RequireAsync(actor, tenantId, TenantRole.Admin, ct);
        var rows = await db.ApiKeys.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(ct);
        return rows.OrderByDescending(x => x.CreatedAt)
            .Select(x => new ApiKeySummary(x.Id, x.Name, x.KeyPrefix, SplitScopes(x.Scopes), x.KnowledgeBaseId, x.IsActive, x.CreatedAt, x.ExpiresAt, x.LastUsedAt))
            .ToList();
    }

    public async Task<CreatedApiKeyResponse> CreateAsync(ActorContext actor, Guid tenantId, CreateApiKeyRequest request, CancellationToken ct = default)
    {
        if (actor.IsApiKey) throw new UnauthorizedAccessException("API Key 不能创建其他 API Key。");
        await access.RequireAsync(actor, tenantId, TenantRole.Admin, ct);
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length is < 1 or > 200) throw new InvalidOperationException("API Key 名称长度必须在 1-200 个字符之间。");
        var scopes = request.Scopes?.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (scopes.Length == 0 || scopes.Any(x => !ApiKeyScopes.Allowed.Contains(x, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Scopes 只能是：{string.Join(", ", ApiKeyScopes.Allowed)}");
        if (request.KnowledgeBaseId.HasValue && !await db.KnowledgeBases.AnyAsync(x => x.Id == request.KnowledgeBaseId && x.TenantId == tenantId, ct))
            throw new InvalidOperationException("指定的知识库不属于该租户。");
        if (request.ExpiresAt.HasValue && request.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("过期时间必须晚于当前时间。");

        var raw = "kr_live_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var entity = new ApiKeyEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, KnowledgeBaseId = request.KnowledgeBaseId, CreatedByUserId = actor.UserId,
            Name = name, KeyPrefix = raw[..Math.Min(16, raw.Length)], KeyHash = Hash(raw), Scopes = string.Join(' ', scopes),
            IsActive = true, CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = request.ExpiresAt
        };
        db.ApiKeys.Add(entity);
        await db.SaveChangesAsync(ct);
        return new CreatedApiKeyResponse(entity.Id, entity.Name, raw, scopes, entity.KnowledgeBaseId, entity.ExpiresAt);
    }

    public async Task RevokeAsync(ActorContext actor, Guid tenantId, Guid apiKeyId, CancellationToken ct = default)
    {
        if (actor.IsApiKey) throw new UnauthorizedAccessException("API Key 不能撤销其他 API Key。");
        await access.RequireAsync(actor, tenantId, TenantRole.Admin, ct);
        var key = await db.ApiKeys.SingleOrDefaultAsync(x => x.Id == apiKeyId && x.TenantId == tenantId, ct);
        if (key is null) return;
        key.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    internal static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    internal static string[] SplitScopes(string scopes) => scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
