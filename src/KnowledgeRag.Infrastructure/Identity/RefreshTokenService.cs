using System.Security.Cryptography;
using System.Text;
using KnowledgeRag.Infrastructure.Persistence;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Application.Security;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.Identity;

public sealed class RefreshTokenService(
    AppDbContext db,
    JwtTokenService jwt,
    IOptions<AuthOptions> options)
{
    private readonly AuthOptions _options = options.Value;

    public async Task<AuthResponse> IssueAsync(
        UserEntity user,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var access = jwt.CreateAccessToken(user);
        var rawRefresh = CreateOpaqueToken();
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddDays(Math.Clamp(_options.RefreshTokenLifetimeDays, 1, 365));
        db.RefreshTokens.Add(new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = Hash(rawRefresh),
            CreatedAt = now,
            ExpiresAt = expires,
            CreatedByIp = Trim(ip, 128),
            UserAgent = Trim(userAgent, 512)
        });
        await db.SaveChangesAsync(cancellationToken);
        return new AuthResponse(access.Token, access.ExpiresAt, rawRefresh, expires, new UserSummary(user.Id, user.Email, user.DisplayName));
    }

    public async Task<AuthResponse> RotateAsync(
        string rawToken,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) throw new UnauthorizedAccessException("Refresh Token 无效。");
        var tokenHash = Hash(rawToken);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken)
            ?? throw new UnauthorizedAccessException("Refresh Token 无效。");

        if (token.RevokedAt.HasValue)
        {
            await RevokeAllAsync(token.UserId, cancellationToken);
            throw new UnauthorizedAccessException("检测到已撤销 Refresh Token 被重复使用，已撤销该用户的全部会话。");
        }
        if (token.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new UnauthorizedAccessException("Refresh Token 已过期。");

        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == token.UserId && x.IsActive, cancellationToken)
            ?? throw new UnauthorizedAccessException("用户不存在或已停用。");

        var access = jwt.CreateAccessToken(user);
        var newRaw = CreateOpaqueToken();
        var now = DateTimeOffset.UtcNow;
        var newEntity = new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = Hash(newRaw),
            CreatedAt = now,
            ExpiresAt = now.AddDays(Math.Clamp(_options.RefreshTokenLifetimeDays, 1, 365)),
            CreatedByIp = Trim(ip, 128),
            UserAgent = Trim(userAgent, 512)
        };
        token.RevokedAt = now;
        token.ReplacedByTokenId = newEntity.Id;
        db.RefreshTokens.Add(newEntity);
        await db.SaveChangesAsync(cancellationToken);
        return new AuthResponse(access.Token, access.ExpiresAt, newRaw, newEntity.ExpiresAt, new UserSummary(user.Id, user.Email, user.DisplayName));
    }

    public async Task RevokeAsync(string rawToken, Guid? userId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return;
        var hash = Hash(rawToken);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (token is null || (userId.HasValue && token.UserId != userId.Value)) return;
        if (!token.RevokedAt.HasValue)
        {
            token.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        var active = await db.RefreshTokens.Where(x => x.UserId == userId && x.RevokedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow).ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var item in active) item.RevokedAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string CreateOpaqueToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string? Trim(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];
}
