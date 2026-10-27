using KnowledgeRag.Infrastructure.AI;
using KnowledgeRag.Infrastructure.Configuration;
using KnowledgeRag.Infrastructure.Conversations;
using KnowledgeRag.Infrastructure.Documents;
using KnowledgeRag.Infrastructure.Identity;
using KnowledgeRag.Infrastructure.KnowledgeBases;
using KnowledgeRag.Infrastructure.Messaging;
using KnowledgeRag.Infrastructure.Observability;
using KnowledgeRag.Infrastructure.Persistence;
using KnowledgeRag.Infrastructure.Search;
using KnowledgeRag.Infrastructure.Storage;
using KnowledgeRag.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KnowledgeRag.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.Configure<OpenTelemetryOptions>(configuration.GetSection(OpenTelemetryOptions.SectionName));
        services.AddSingleton<ProductMetricsCollector>();
        services.AddScoped<DashboardService>();
        services.AddScoped<ProductSettingsService>();
        services.AddPersistence(configuration, environment);
        services.AddAiModule(configuration);
        services.AddStorageModule(configuration);
        services.AddSearchModule(configuration);
        services.AddIdentityModule(configuration);
        services.AddTenancyModule();
        services.AddKnowledgeBaseModule();
        services.AddDocumentModule(configuration);
        services.AddConversationModule();
        services.AddMessagingModule(configuration);
        return services;
    }

    public static async Task InitializeInfrastructureAsync(this IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var database = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        var searchBackends = configuration.GetSection(SearchBackendOptions.SectionName).Get<SearchBackendOptions>() ?? new SearchBackendOptions();
        await using var scope = services.CreateAsyncScope();
        if (database.AutoMigrate)
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync(cancellationToken);
        }
        if (!searchBackends.AutoInitializeNativeIndexes) return;
        if (searchBackends.Vector.Equals("pgvector", StringComparison.OrdinalIgnoreCase))
            await scope.ServiceProvider.GetRequiredService<PgVectorStore>().EnsureReadyAsync(cancellationToken);
        if (searchBackends.Lexical.Equals("postgres_fts", StringComparison.OrdinalIgnoreCase))
            await scope.ServiceProvider.GetRequiredService<PostgresFtsSearchService>().EnsureReadyAsync(cancellationToken);
    }
}
