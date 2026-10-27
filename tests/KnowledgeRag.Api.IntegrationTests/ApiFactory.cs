using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _rootPath;
    private readonly string _dbPath;

    public ApiFactory()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), $"knowledge-rag-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootPath);
        _dbPath = Path.Combine(_rootPath, "knowledge.db");

        // WebApplicationFactory applies ConfigureAppConfiguration late in the minimal-hosting
        // pipeline. These environment variables are available before Program registers
        // Infrastructure, so services that eagerly read configuration see the test values.
        Environment.SetEnvironmentVariable("Database__Provider", "Sqlite");
        Environment.SetEnvironmentVariable("Database__ConnectionString", $"Data Source={_dbPath}");
        Environment.SetEnvironmentVariable("Database__AutoMigrate", "true");
        Environment.SetEnvironmentVariable("SearchBackend__Vector", "qdrant");
        Environment.SetEnvironmentVariable("SearchBackend__Lexical", "bm25");
        Environment.SetEnvironmentVariable("SearchBackend__AutoInitializeNativeIndexes", "false");
        Environment.SetEnvironmentVariable("Storage__Provider", "local");
        Environment.SetEnvironmentVariable("Storage__RootPath", _rootPath);
        Environment.SetEnvironmentVariable("OpenTelemetry__Enabled", "false");
        Environment.SetEnvironmentVariable("Auth__SigningKey", "integration-test-signing-key-that-is-longer-than-32-bytes");
        Environment.SetEnvironmentVariable("Auth__AccessTokenLifetimeMinutes", "15");
        Environment.SetEnvironmentVariable("Auth__RefreshTokenLifetimeDays", "7");
    }

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
                ["Storage:RootPath"] = _rootPath,
                ["OpenTelemetry:Enabled"] = "false"
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        Environment.SetEnvironmentVariable("Database__Provider", null);
        Environment.SetEnvironmentVariable("Database__ConnectionString", null);
        Environment.SetEnvironmentVariable("Database__AutoMigrate", null);
        Environment.SetEnvironmentVariable("SearchBackend__Vector", null);
        Environment.SetEnvironmentVariable("SearchBackend__Lexical", null);
        Environment.SetEnvironmentVariable("SearchBackend__AutoInitializeNativeIndexes", null);
        Environment.SetEnvironmentVariable("Storage__Provider", null);
        Environment.SetEnvironmentVariable("Storage__RootPath", null);
        Environment.SetEnvironmentVariable("OpenTelemetry__Enabled", null);
        Environment.SetEnvironmentVariable("Auth__SigningKey", null);
        Environment.SetEnvironmentVariable("Auth__AccessTokenLifetimeMinutes", null);
        Environment.SetEnvironmentVariable("Auth__RefreshTokenLifetimeDays", null);

        if (Directory.Exists(_rootPath))
            Directory.Delete(_rootPath, recursive: true);
    }
}
