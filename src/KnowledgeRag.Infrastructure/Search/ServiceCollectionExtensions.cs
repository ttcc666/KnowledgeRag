using KnowledgeRag.Application.Abstractions;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeRag.Infrastructure.Search;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSearchModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<QdrantOptions>(configuration.GetSection(QdrantOptions.SectionName));
        services.Configure<RerankerOptions>(configuration.GetSection(RerankerOptions.SectionName));
        services.Configure<SearchBackendOptions>(configuration.GetSection(SearchBackendOptions.SectionName));
        var backends = configuration.GetSection(SearchBackendOptions.SectionName).Get<SearchBackendOptions>() ?? new SearchBackendOptions();

        services.AddHttpClient<QdrantVectorStore>();
        services.AddScoped<PgVectorStore>();
        services.AddScoped<PostgresFtsSearchService>();
        services.AddScoped<Bm25SearchService>();
        services.AddScoped<IVectorStore>(sp => backends.Vector.Equals("pgvector", StringComparison.OrdinalIgnoreCase)
            ? sp.GetRequiredService<PgVectorStore>()
            : sp.GetRequiredService<QdrantVectorStore>());
        services.AddScoped<ILexicalSearchService>(sp => backends.Lexical.Equals("postgres_fts", StringComparison.OrdinalIgnoreCase)
            ? sp.GetRequiredService<PostgresFtsSearchService>()
            : sp.GetRequiredService<Bm25SearchService>());
        services.AddScoped<RerankerService>();
        services.AddScoped<KnowledgeSearchService>();
        services.AddScoped<MultiQuerySearchService>();
        return services;
    }
}
