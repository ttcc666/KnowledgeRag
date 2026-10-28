using KnowledgeRag.Infrastructure.Configuration;
using KnowledgeRag.Infrastructure.Governance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeRag.Infrastructure.AI;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAiModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.Configure<SecretProtectionOptions>(configuration.GetSection(SecretProtectionOptions.SectionName));
        services.AddSingleton<AiCredentialProtector>();
        services.AddSingleton<AiProviderClientFactory>();
        services.AddHttpClient("tenant-ai-provider", client => client.Timeout = TimeSpan.FromSeconds(60))
            .ConfigurePrimaryHttpMessageHandler(WebhookSafeConnection.CreateHttpHandler);
        services.AddHttpClient("platform-ai-provider", client => client.Timeout = TimeSpan.FromSeconds(60));
        services.AddHttpClient<OpenAiCompatibleEmbeddingService>();
        services.AddScoped<OpenAiCompatibleRerankerService>();
        return services;
    }
}
