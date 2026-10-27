using System.Security.Claims;
using KnowledgeRag.Api.Security;

namespace KnowledgeRag.Api.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard", async (ClaimsPrincipal principal, DashboardService service, CancellationToken ct)
            => Results.Ok(await service.GetSummaryAsync(HttpActorContext.From(principal), ct)))
            .RequireAuthorization().WithTags("Operations");

        app.MapGet("/api/ingestion/jobs", async (ClaimsPrincipal principal, DashboardService service, CancellationToken ct)
            => Results.Ok(await service.ListJobsAsync(HttpActorContext.From(principal), ct)))
            .RequireAuthorization().WithTags("Operations");

        app.MapGet("/api/settings/product", (ProductSettingsService service) => Results.Ok(service.Get()))
            .RequireAuthorization().WithTags("Operations");

        return app;
    }
}
