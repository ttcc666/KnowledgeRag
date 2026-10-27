namespace KnowledgeRag.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app, IConfiguration configuration)
    {
        var database = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        var storage = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
        var search = configuration.GetSection(SearchBackendOptions.SectionName).Get<SearchBackendOptions>() ?? new SearchBackendOptions();

        app.MapGet("/api/health", () => Results.Ok(new
        {
            status = "ok",
            database = database.Provider,
            vectorBackend = search.Vector,
            lexicalBackend = search.Lexical,
            storage = storage.Provider,
            utc = DateTimeOffset.UtcNow
        }));

        app.MapGet("/api/health/ready", async (DatabaseHealthService health, CancellationToken ct) =>
            await health.CanConnectAsync(ct)
                ? Results.Ok(new { status = "ready" })
                : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

        return app;
    }
}
