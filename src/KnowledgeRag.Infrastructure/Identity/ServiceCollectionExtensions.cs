using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeRag.Infrastructure.Identity;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.AddSingleton<PasswordService>();
        services.AddSingleton<JwtTokenService>();
        services.AddScoped<RefreshTokenService>();
        services.AddScoped<AuthService>();
        services.AddScoped<ApiKeyService>();
        return services;
    }
}
