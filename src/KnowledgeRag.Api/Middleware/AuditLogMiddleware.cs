using KnowledgeRag.Api.Security;
using KnowledgeRag.Infrastructure.Governance;

namespace KnowledgeRag.Api.Middleware;

public sealed class AuditLogMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, GovernanceService governance)
    {
        if (!IsAuditable(context.Request) || context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        Exception? failure = null;
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            try
            {
                var actor = HttpActorContext.From(context.User);
                var tenantId = actor.TenantId
                               ?? TryGuid(context.Request.RouteValues["tenantId"]?.ToString())
                               ?? TryGuid(context.Request.Query["tenantId"].FirstOrDefault());
                var knowledgeBaseId = TryGuid(context.Request.RouteValues["knowledgeBaseId"]?.ToString())
                                      ?? TryGuid(context.Request.Query["knowledgeBaseId"].FirstOrDefault());
                if (!tenantId.HasValue && knowledgeBaseId.HasValue)
                    tenantId = await governance.ResolveTenantIdAsync(knowledgeBaseId.Value, CancellationToken.None);

                var path = context.Request.Path.Value ?? string.Empty;
                var resourceId = context.Request.RouteValues.Values.Select(x => x?.ToString()).LastOrDefault(x => Guid.TryParse(x, out _));
                var action = context.Request.Method.ToUpperInvariant() switch
                {
                    "POST" => "create_or_execute",
                    "PUT" => "update",
                    "PATCH" => "update",
                    "DELETE" => "delete",
                    _ => context.Request.Method.ToLowerInvariant()
                };
                await governance.WriteAuditAsync(
                    actor,
                    tenantId,
                    action,
                    ResourceType(path),
                    resourceId,
                    $"{context.Request.Method} {path}",
                    failure is null ? context.Response.StatusCode : 500,
                    context.Connection.RemoteIpAddress?.ToString(),
                    context.Request.Headers.UserAgent.ToString(),
                    new { route = path, failed = failure is not null, errorType = failure?.GetType().Name },
                    CancellationToken.None);
            }
            catch
            {
                // Audit persistence must never change the outcome of the business request.
            }
        }
    }

    private static bool IsAuditable(HttpRequest request)
        => request.Path.StartsWithSegments("/api")
           && request.Method is "POST" or "PUT" or "PATCH" or "DELETE"
           && !request.Path.StartsWithSegments("/api/auth/refresh");

    private static Guid? TryGuid(string? value) => Guid.TryParse(value, out var parsed) ? parsed : null;

    private static string ResourceType(string path)
    {
        if (path.Contains("/documents", StringComparison.OrdinalIgnoreCase)) return "document";
        if (path.Contains("/conversations", StringComparison.OrdinalIgnoreCase)) return "conversation";
        if (path.Contains("/knowledge-bases", StringComparison.OrdinalIgnoreCase)) return "knowledge-base";
        if (path.Contains("/evaluation", StringComparison.OrdinalIgnoreCase)) return "evaluation";
        if (path.Contains("/webhooks", StringComparison.OrdinalIgnoreCase)) return "webhook";
        if (path.Contains("/prompts", StringComparison.OrdinalIgnoreCase)) return "prompt-profile";
        if (path.Contains("/model-prices", StringComparison.OrdinalIgnoreCase)) return "model-governance";
        if (path.Contains("/tenants", StringComparison.OrdinalIgnoreCase)) return "tenant";
        return "api";
    }
}
