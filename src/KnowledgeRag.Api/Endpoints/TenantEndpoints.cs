using System.Security.Claims;
using KnowledgeRag.Api.Security;

namespace KnowledgeRag.Api.Endpoints;

public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tenants").RequireAuthorization().WithTags("Tenants");

        group.MapGet("", async (ClaimsPrincipal principal, TenantService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(HttpActorContext.From(principal), ct)));

        group.MapPost("", async (ClaimsPrincipal principal, CreateTenantRequest request, TenantService service, CancellationToken ct) =>
            Results.Ok(await service.CreateAsync(HttpActorContext.From(principal), request, ct)));

        group.MapPut("/{tenantId:guid}", async (Guid tenantId, ClaimsPrincipal principal, UpdateTenantRequest request, TenantService service, CancellationToken ct) =>
        {
            await service.UpdateAsync(HttpActorContext.From(principal), tenantId, request, ct);
            return Results.NoContent();
        });

        group.MapDelete("/{tenantId:guid}", async (Guid tenantId, ClaimsPrincipal principal, TenantService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(HttpActorContext.From(principal), tenantId, ct);
            return Results.NoContent();
        });

        group.MapGet("/{tenantId:guid}/members", async (Guid tenantId, ClaimsPrincipal principal, TenantService service, CancellationToken ct) =>
            Results.Ok(await service.ListMembersAsync(HttpActorContext.From(principal), tenantId, ct)));

        group.MapPut("/{tenantId:guid}/members", async (Guid tenantId, ClaimsPrincipal principal, AddTenantMemberRequest request, TenantService service, CancellationToken ct) =>
        {
            await service.AddOrUpdateMemberAsync(HttpActorContext.From(principal), tenantId, request, ct);
            return Results.NoContent();
        });

        group.MapDelete("/{tenantId:guid}/members/{memberUserId:guid}", async (Guid tenantId, Guid memberUserId, ClaimsPrincipal principal, TenantService service, CancellationToken ct) =>
        {
            await service.RemoveMemberAsync(HttpActorContext.From(principal), tenantId, memberUserId, ct);
            return Results.NoContent();
        });

        group.MapGet("/{tenantId:guid}/api-keys", async (Guid tenantId, ClaimsPrincipal principal, ApiKeyService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(HttpActorContext.From(principal), tenantId, ct)));

        group.MapPost("/{tenantId:guid}/api-keys", async (Guid tenantId, ClaimsPrincipal principal, CreateApiKeyRequest request, ApiKeyService service, CancellationToken ct) =>
            Results.Ok(await service.CreateAsync(HttpActorContext.From(principal), tenantId, request, ct)));

        group.MapDelete("/{tenantId:guid}/api-keys/{apiKeyId:guid}", async (Guid tenantId, Guid apiKeyId, ClaimsPrincipal principal, ApiKeyService service, CancellationToken ct) =>
        {
            await service.RevokeAsync(HttpActorContext.From(principal), tenantId, apiKeyId, ct);
            return Results.NoContent();
        });

        group.MapGet("/ai/providers", (TenantAiProviderService service) => Results.Ok(service.ListProviderCatalog()));

        group.MapGet("/{tenantId:guid}/ai/connections", async (Guid tenantId, ClaimsPrincipal principal, TenantAiProviderService service, CancellationToken ct) =>
            Results.Ok(await service.ListConnectionsAsync(HttpActorContext.From(principal), tenantId, ct)));

        group.MapPost("/{tenantId:guid}/ai/connections", async (Guid tenantId, ClaimsPrincipal principal, SaveAiProviderConnectionRequest request, TenantAiProviderService service, CancellationToken ct) =>
            Results.Ok(await service.SaveConnectionAsync(HttpActorContext.From(principal), tenantId, request, ct)));

        group.MapDelete("/{tenantId:guid}/ai/connections/{connectionId:guid}", async (Guid tenantId, Guid connectionId, ClaimsPrincipal principal, TenantAiProviderService service, CancellationToken ct) =>
        {
            await service.DeleteConnectionAsync(HttpActorContext.From(principal), tenantId, connectionId, ct);
            return Results.NoContent();
        });

        group.MapPost("/{tenantId:guid}/ai/connections/{connectionId:guid}/test", async (Guid tenantId, Guid connectionId, ClaimsPrincipal principal, TenantAiProviderService service, CancellationToken ct) =>
            Results.Ok(await service.TestConnectionAsync(HttpActorContext.From(principal), tenantId, connectionId, ct)));

        group.MapGet("/{tenantId:guid}/ai/deployments", async (Guid tenantId, ClaimsPrincipal principal, TenantAiProviderService service, CancellationToken ct) =>
            Results.Ok(await service.ListDeploymentsAsync(HttpActorContext.From(principal), tenantId, ct)));

        group.MapPost("/{tenantId:guid}/ai/deployments", async (Guid tenantId, ClaimsPrincipal principal, SaveAiModelDeploymentRequest request, TenantAiProviderService service, CancellationToken ct) =>
            Results.Ok(await service.SaveDeploymentAsync(HttpActorContext.From(principal), tenantId, request, ct)));

        group.MapDelete("/{tenantId:guid}/ai/deployments/{deploymentId:guid}", async (Guid tenantId, Guid deploymentId, ClaimsPrincipal principal, TenantAiProviderService service, CancellationToken ct) =>
        {
            await service.DeleteDeploymentAsync(HttpActorContext.From(principal), tenantId, deploymentId, ct);
            return Results.NoContent();
        });

        group.MapPost("/{tenantId:guid}/ai/deployments/{deploymentId:guid}/health", async (Guid tenantId, Guid deploymentId, ClaimsPrincipal principal, TenantAiProviderService service, CancellationToken ct) =>
            Results.Ok(await service.TestDeploymentHealthAsync(HttpActorContext.From(principal), tenantId, deploymentId, ct)));

        group.MapGet("/{tenantId:guid}/ai/defaults", async (Guid tenantId, ClaimsPrincipal principal, TenantAiProviderService service, CancellationToken ct) =>
            Results.Ok(await service.GetDefaultsAsync(HttpActorContext.From(principal), tenantId, ct)));

        group.MapPut("/{tenantId:guid}/ai/defaults", async (Guid tenantId, ClaimsPrincipal principal, SaveTenantAiDefaultsRequest request, TenantAiProviderService service, CancellationToken ct) =>
            Results.Ok(await service.SaveDefaultsAsync(HttpActorContext.From(principal), tenantId, request, ct)));

        return app;
    }
}
