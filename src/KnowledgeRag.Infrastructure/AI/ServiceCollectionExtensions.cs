using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeRag.Infrastructure.AI;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAiModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.AddHttpClient<OpenAiCompatibleEmbeddingService>();
        services.AddHttpClient<OpenAiCompatibleRerankerService>();
        return services;
    }
}
