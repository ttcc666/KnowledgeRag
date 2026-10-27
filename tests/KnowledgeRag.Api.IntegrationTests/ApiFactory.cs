using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"knowledge-rag-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["Database:ConnectionString"] = $"Data Source={_dbPath}",
                ["Database:AutoMigrate"] = "true",
                ["Auth:SigningKey"] = "integration-test-signing-key-that-is-longer-than-32-bytes",
                ["Auth:AccessTokenLifetimeMinutes"] = "15",
                ["Auth:RefreshTokenLifetimeDays"] = "7",
                ["SearchBackend:Vector"] = "qdrant",
                ["SearchBackend:Lexical"] = "bm25",
                ["SearchBackend:AutoInitializeNativeIndexes"] = "false",
                ["Storage:Provider"] = "local",
                ["OpenTelemetry:Enabled"] = "false"
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
