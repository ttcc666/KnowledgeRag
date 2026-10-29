using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeRag.Infrastructure.Documents;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDocumentModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RagOptions>(configuration.GetSection(RagOptions.SectionName));
        services.AddSingleton<DocumentParser>();
        services.AddSingleton<TextChunker>();
        services.AddScoped<KnowledgeIngestionService>();
        services.AddScoped<KnowledgeBaseIndexGenerationService>();
        return services;
    }
}
