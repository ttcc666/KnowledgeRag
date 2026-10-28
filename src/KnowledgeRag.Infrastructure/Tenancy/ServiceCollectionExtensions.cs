using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeRag.Infrastructure.Tenancy;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTenancyModule(this IServiceCollection services)
    {
        services.AddScoped<TenantAccessService>();
        services.AddScoped<TenantService>();
        services.AddScoped<TenantAiProviderService>();
        return services;
    }
}
