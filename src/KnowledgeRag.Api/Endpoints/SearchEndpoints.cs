using System.Security.Claims;
using KnowledgeRag.Api.Security;

namespace KnowledgeRag.Api.Endpoints;

public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/search", async (SearchRequest request, ClaimsPrincipal principal, KnowledgeBaseAccessService access, KnowledgeSearchService search, CancellationToken ct) =>
        {
            var actor = HttpActorContext.From(principal);
            await access.RequireAsync(actor, request.KnowledgeBaseId, KnowledgeBaseRole.Viewer, ct);
            return Results.Ok(await search.SearchAsync(request.KnowledgeBaseId, request.Query, request.TopK, ct));
        }).RequireAuthorization().WithTags("Search");

        return app;
    }
}
