using System.Security.Claims;
using KnowledgeRag.Api.Security;

namespace KnowledgeRag.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", async (RegisterRequest request, HttpContext http, AuthService service, CancellationToken ct) =>
            Results.Ok(await service.RegisterAsync(request, http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers["User-Agent"].ToString(), ct)));

        group.MapPost("/login", async (LoginRequest request, HttpContext http, AuthService service, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.LoginAsync(request, http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers["User-Agent"].ToString(), ct));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status401Unauthorized);
            }
        });

        group.MapPost("/refresh", async (RefreshTokenRequest request, HttpContext http, RefreshTokenService service, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.RotateAsync(request.RefreshToken, http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers["User-Agent"].ToString(), ct));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status401Unauthorized);
            }
        });

        group.MapPost("/logout", async (RefreshTokenRequest request, ClaimsPrincipal principal, RefreshTokenService service, CancellationToken ct) =>
        {
            await service.RevokeAsync(request.RefreshToken, HttpActorContext.GetUserId(principal), ct);
            return Results.NoContent();
        }).RequireAuthorization();

        group.MapGet("/me", async (ClaimsPrincipal principal, AuthService service, CancellationToken ct) =>
        {
            var actor = HttpActorContext.From(principal);
            var user = await service.GetUserAsync(actor, ct);
            return user is null
                ? Results.NotFound()
                : Results.Ok(new { user.Id, user.Email, user.DisplayName, actor.ApiKeyId, actor.TenantId, actor.Scopes });
        }).RequireAuthorization();

        return app;
    }
}
