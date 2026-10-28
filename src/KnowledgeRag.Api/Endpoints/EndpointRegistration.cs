using KnowledgeRag.Api.Realtime;

namespace KnowledgeRag.Api.Endpoints;

public static class EndpointRegistration
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app, IConfiguration configuration)
    {
        app.MapHealthEndpoints(configuration);
        app.MapAuthEndpoints();
        app.MapTenantEndpoints();
        app.MapKnowledgeBaseEndpoints();
        app.MapDocumentEndpoints();
        app.MapSearchEndpoints();
        app.MapConversationEndpoints();
        app.MapDashboardEndpoints();
        app.MapGovernanceEndpoints();
        app.MapHub<DocumentProcessingHub>("/hubs/documents");
        return app;
    }
}
