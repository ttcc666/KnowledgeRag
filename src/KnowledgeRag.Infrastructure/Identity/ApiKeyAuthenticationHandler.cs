using System.Security.Claims;
using System.Text.Encodings.Web;
using KnowledgeRag.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.Identity;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AppDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var rawValues)) return AuthenticateResult.NoResult();
        var raw = rawValues.ToString().Trim();
        if (string.IsNullOrWhiteSpace(raw)) return AuthenticateResult.Fail("API Key 为空。");
        var hash = ApiKeyService.Hash(raw);
        var now = DateTimeOffset.UtcNow;
        var key = await db.ApiKeys.SingleOrDefaultAsync(x => x.KeyHash == hash, Context.RequestAborted);
        if (key is null || !key.IsActive || (key.ExpiresAt.HasValue && key.ExpiresAt <= now))
            return AuthenticateResult.Fail("API Key 无效或已过期。");

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == key.CreatedByUserId && x.IsActive, Context.RequestAborted);
        if (user is null) return AuthenticateResult.Fail("API Key 所属用户已停用。");

        key.LastUsedAt = now;
        await db.SaveChangesAsync(Context.RequestAborted);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Name, $"api-key:{key.Name}"),
            new("kr:auth_type", "api_key"), new("kr:api_key_id", key.Id.ToString()), new("kr:tenant_id", key.TenantId.ToString())
        };
        if (key.KnowledgeBaseId.HasValue) claims.Add(new Claim("kr:knowledge_base_id", key.KnowledgeBaseId.Value.ToString()));
        claims.AddRange(ApiKeyService.SplitScopes(key.Scopes).Select(scope => new Claim("kr:scope", scope)));
        var identity = new ClaimsIdentity(claims, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
