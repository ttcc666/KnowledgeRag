using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KnowledgeRag.Infrastructure.Persistence;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var provider = Environment.GetEnvironmentVariable("DATABASE_PROVIDER") ?? "Sqlite";
        var connection = Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING")
            ?? "Data Source=App_Data/knowledge.db";
        var options = new DbContextOptionsBuilder<AppDbContext>();
        if (provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase) || provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
            options.UseNpgsql(connection);
        else
            options.UseSqlite(connection);
        return new AppDbContext(options.Options);
    }
}
