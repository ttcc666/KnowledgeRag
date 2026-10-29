using KnowledgeRag.Api.Middleware;
using Microsoft.Extensions.Hosting;

namespace KnowledgeRag.Api.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        // Only honor forwarded headers from loopback or explicitly configured trusted proxies.
        app.UseForwardedHeaders();
        if (app.Environment.IsProduction()) app.UseHsts();
        if (app.Configuration.GetValue<bool>("Security:EnforceHttpsRedirection"))
            app.UseHttpsRedirection();

        app.UseMiddleware<ApiExceptionMiddleware>();
        if (ShouldExposeSwagger(app.Environment, app.Configuration))
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseCors("ConfiguredOrigins");
        app.UseAuthentication();
        // Keep audit outside authorization and rate limiting so authenticated 401/403/429
        // responses are visible to the audit pipeline.
        app.UseMiddleware<AuditLogMiddleware>();
        app.UseRateLimiter();
        app.UseAuthorization();
        return app;
    }

    public static bool ShouldExposeSwagger(IHostEnvironment environment, IConfiguration configuration)
        => environment.IsDevelopment() || configuration.GetValue<bool>("Swagger:Enabled");
}
