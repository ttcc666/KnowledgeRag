namespace KnowledgeRag.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddApiAuthentication(configuration);
        services.AddApiSwagger();
        services.AddApiTelemetry(configuration);
        return services;
    }
}
