using System.Text.RegularExpressions;
using KnowledgeRag.Infrastructure.Persistence;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Application.Security;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.Identity;

public sealed class AuthService(
    AppDbContext db,
    PasswordService passwords,
    RefreshTokenService tokens,
    IOptions<AuthOptions> options)
{
    private readonly AuthOptions _options = options.Value;

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ip, string? userAgent, CancellationToken cancellationToken = default)
    {
        if (!_options.AllowRegistration) throw new InvalidOperationException("当前已关闭公开注册。");
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            throw new InvalidOperationException("密码至少需要 8 个字符。");

        var email = NormalizeEmail(request.Email);
        var normalized = email.ToUpperInvariant();
        if (await db.Users.AnyAsync(x => x.NormalizedEmail == normalized, cancellationToken))
            throw new InvalidOperationException("该邮箱已注册。");

        var displayName = NormalizeDisplayName(request.DisplayName, email);
        var (hash, salt) = passwords.Hash(request.Password);
        var now = DateTimeOffset.UtcNow;
        var user = new UserEntity
        {
            Id = Guid.NewGuid(), Email = email, NormalizedEmail = normalized, DisplayName = displayName,
            PasswordHash = hash, PasswordSalt = salt, CreatedAt = now, IsActive = true
        };
        var tenant = new TenantEntity
        {
            Id = Guid.NewGuid(), Name = $"{displayName} 的空间", Slug = await CreateUniqueSlugAsync(displayName, cancellationToken),
            CreatedByUserId = user.Id, CreatedAt = now, UpdatedAt = now
        };
        db.Users.Add(user);
        db.Tenants.Add(tenant);
        db.TenantMembers.Add(new TenantMemberEntity { TenantId = tenant.Id, UserId = user.Id, Role = TenantRole.Owner, AddedAt = now });
        await db.SaveChangesAsync(cancellationToken);
        return await tokens.IssueAsync(user, ip, userAgent, cancellationToken);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? ip, string? userAgent, CancellationToken cancellationToken = default)
    {
        var email = NormalizeEmail(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
        if (user is null || !user.IsActive || !passwords.Verify(request.Password, user.PasswordHash, user.PasswordSalt))
            throw new UnauthorizedAccessException("邮箱或密码错误。");
        return await tokens.IssueAsync(user, ip, userAgent, cancellationToken);
    }

    public async Task<UserSummary?> GetUserAsync(ActorContext actor, CancellationToken cancellationToken = default)
    {
        return await db.Users.AsNoTracking()
            .Where(x => x.Id == actor.UserId)
            .Select(x => new UserSummary(x.Id, x.Email, x.DisplayName))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<string> CreateUniqueSlugAsync(string value, CancellationToken ct)
    {
        var slug = Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9\u4e00-\u9fff]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(slug)) slug = "tenant";
        if (slug.Length > 80) slug = slug[..80];
        var candidate = slug;
        for (var i = 0; await db.Tenants.AnyAsync(x => x.Slug == candidate, ct); i++) candidate = $"{slug}-{Guid.NewGuid():N}"[..Math.Min(100, slug.Length + 9)];
        return candidate;
    }

    private static string NormalizeDisplayName(string? displayName, string email)
    {
        var value = string.IsNullOrWhiteSpace(displayName) ? email.Split('@')[0] : displayName.Trim();
        if (value.Length > 200) throw new InvalidOperationException("显示名不能超过 200 个字符。");
        return value;
    }

    private static string NormalizeEmail(string email)
    {
        var value = (email ?? string.Empty).Trim();
        if (value.Length is < 3 or > 320 || !value.Contains('@')) throw new InvalidOperationException("邮箱格式不正确。");
        return value;
    }
}
