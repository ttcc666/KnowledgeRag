using System.Security.Claims;
using KnowledgeRag.Api.Security;

namespace KnowledgeRag.Api.Endpoints;

public static class KnowledgeBaseEndpoints
{
    public static IEndpointRouteBuilder MapKnowledgeBaseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/knowledge-bases").RequireAuthorization().WithTags("Knowledge Bases");

        group.MapGet("", async (ClaimsPrincipal principal, KnowledgeBaseService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(HttpActorContext.From(principal), ct)));

        group.MapPost("", async (ClaimsPrincipal principal, CreateKnowledgeBaseRequest request, KnowledgeBaseService service, CancellationToken ct) =>
            Results.Ok(await service.CreateAsync(HttpActorContext.From(principal), request, ct)));

        group.MapGet("/{knowledgeBaseId:guid}", async (Guid knowledgeBaseId, ClaimsPrincipal principal, KnowledgeBaseService service, CancellationToken ct) =>
        {
            var kb = await service.GetAsync(HttpActorContext.From(principal), knowledgeBaseId, ct);
            return kb is null ? Results.NotFound() : Results.Ok(kb);
        });

        group.MapPut("/{knowledgeBaseId:guid}", async (Guid knowledgeBaseId, ClaimsPrincipal principal, UpdateKnowledgeBaseRequest request, KnowledgeBaseService service, CancellationToken ct) =>
        {
            await service.UpdateAsync(HttpActorContext.From(principal), knowledgeBaseId, request, ct);
            return Results.NoContent();
        });

        group.MapDelete("/{knowledgeBaseId:guid}", async (Guid knowledgeBaseId, ClaimsPrincipal principal, KnowledgeBaseService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(HttpActorContext.From(principal), knowledgeBaseId, ct);
            return Results.NoContent();
        });

        group.MapGet("/{knowledgeBaseId:guid}/members", async (Guid knowledgeBaseId, ClaimsPrincipal principal, KnowledgeBaseService service, CancellationToken ct) =>
            Results.Ok(await service.ListMembersAsync(HttpActorContext.From(principal), knowledgeBaseId, ct)));

        group.MapPut("/{knowledgeBaseId:guid}/members", async (Guid knowledgeBaseId, ClaimsPrincipal principal, AddKnowledgeBaseMemberRequest request, KnowledgeBaseService service, CancellationToken ct) =>
        {
            await service.AddOrUpdateMemberAsync(HttpActorContext.From(principal), knowledgeBaseId, request, ct);
            return Results.NoContent();
        });

        group.MapDelete("/{knowledgeBaseId:guid}/members/{memberUserId:guid}", async (Guid knowledgeBaseId, Guid memberUserId, ClaimsPrincipal principal, KnowledgeBaseService service, CancellationToken ct) =>
        {
            await service.RemoveMemberAsync(HttpActorContext.From(principal), knowledgeBaseId, memberUserId, ct);
            return Results.NoContent();
        });

        return app;
    }
}
