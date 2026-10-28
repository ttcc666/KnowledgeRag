using KnowledgeRag.Api.Middleware;

namespace KnowledgeRag.Api.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        app.UseMiddleware<ApiExceptionMiddleware>();
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<AuditLogMiddleware>();
        return app;
    }
}
