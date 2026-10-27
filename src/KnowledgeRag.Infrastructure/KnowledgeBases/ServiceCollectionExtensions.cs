using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeRag.Infrastructure.KnowledgeBases;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKnowledgeBaseModule(this IServiceCollection services)
    {
        services.AddScoped<KnowledgeBaseAccessService>();
        services.AddScoped<KnowledgeBaseService>();
        return services;
    }
}
