using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeRag.Infrastructure.Conversations;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddConversationModule(this IServiceCollection services)
    {
        services.AddScoped<RagAgentService>();
        services.AddScoped<ConversationService>();
        return services;
    }
}
