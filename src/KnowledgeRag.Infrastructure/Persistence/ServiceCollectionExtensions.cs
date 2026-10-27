using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KnowledgeRag.Infrastructure.Persistence;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        var database = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();

        Directory.CreateDirectory(Path.Combine(environment.ContentRootPath, "App_Data"));
        services.AddDbContext<AppDbContext>(options =>
        {
            if (IsPostgres(database.Provider)) options.UseNpgsql(database.ConnectionString);
            else options.UseSqlite(database.ConnectionString);
        });
        services.AddScoped<DatabaseHealthService>();
        return services;
    }

    private static bool IsPostgres(string provider)
        => provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase)
           || provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase);
}
