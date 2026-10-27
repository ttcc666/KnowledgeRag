using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeRag.Infrastructure.Storage;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStorageModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<S3Options>(configuration.GetSection(S3Options.SectionName));
        var storage = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();

        services.AddSingleton<LocalFileStorage>();
        services.AddHttpClient<S3FileStorage>();
        services.AddScoped<IFileStorage>(sp => storage.Provider.Equals("s3", StringComparison.OrdinalIgnoreCase)
            ? sp.GetRequiredService<S3FileStorage>()
            : sp.GetRequiredService<LocalFileStorage>());
        return services;
    }
}
