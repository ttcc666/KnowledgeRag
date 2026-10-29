using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class ApiSecurityPipelineTests
{
    [Theory]
    [InlineData("Development", false, true)]
    [InlineData("Testing", false, false)]
    [InlineData("Staging", false, false)]
    [InlineData("Production", false, false)]
    [InlineData("Production", true, true)]
    public void SwaggerExposure_RequiresDevelopmentOrExplicitEnablement(
        string environmentName,
        bool explicitlyEnabled,
        bool expected)
    {
        var environment = new TestHostEnvironment { EnvironmentName = environmentName };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Swagger:Enabled"] = explicitlyEnabled.ToString()
            })
            .Build();

        Assert.Equal(expected, WebApplicationExtensions.ShouldExposeSwagger(environment, configuration));
    }

    [Fact]
    public async Task ConfiguredCorsAndGlobalRateLimiter_AreAppliedToApiRequests()
    {
        const string allowedOrigin = "https://admin.example.test";
        using var factory = new RateLimitApiFactory(allowedOrigin);
        using var client = factory.CreateClient();

        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/health");
        preflight.Headers.Add("Origin", allowedOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        using var preflightResponse = await client.SendAsync(preflight);

        Assert.Equal(HttpStatusCode.NoContent, preflightResponse.StatusCode);
        Assert.Equal(allowedOrigin, Assert.Single(preflightResponse.Headers.GetValues("Access-Control-Allow-Origin")));

        using var first = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var limited = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("60", Assert.Single(limited.Headers.GetValues("Retry-After")));
        Assert.Contains("请求过于频繁", await limited.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthenticationEndpoints_UseStricterPerIpRateLimit()
    {
        using var factory = new RateLimitApiFactory(
            "https://admin.example.test",
            permitLimit: 10_000,
            authPermitLimit: 2);
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await client.PostAsJsonAsync("/api/auth/register", new
            {
                email = $"auth-limit-{Guid.NewGuid():N}@example.test",
                password = "Passw0rd!123",
                displayName = "Rate Limit Test"
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var limited = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"auth-limit-{Guid.NewGuid():N}@example.test",
            password = "Passw0rd!123",
            displayName = "Rate Limit Test"
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("60", Assert.Single(limited.Headers.GetValues("Retry-After")));
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = nameof(Program);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RateLimitApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _rootPath = Path.Combine(Path.GetTempPath(), $"knowledge-rag-rate-limit-{Guid.NewGuid():N}");
        private readonly string _allowedOrigin;
        private readonly int _permitLimit;
        private readonly int _authPermitLimit;

        public RateLimitApiFactory(string allowedOrigin, int permitLimit = 1, int authPermitLimit = 10)
        {
            _allowedOrigin = allowedOrigin;
            _permitLimit = permitLimit;
            _authPermitLimit = authPermitLimit;
            Directory.CreateDirectory(_rootPath);

            // Minimal-hosting bootstrap reads process environment before the late test
            // configuration callback runs, matching the existing ApiFactory setup.
            Environment.SetEnvironmentVariable("Database__Provider", "Sqlite");
            Environment.SetEnvironmentVariable("Database__ConnectionString", $"Data Source={Path.Combine(_rootPath, "knowledge.db")}");
            Environment.SetEnvironmentVariable("Database__AutoMigrate", "true");
            Environment.SetEnvironmentVariable("Auth__SigningKey", "integration-test-signing-key-that-is-longer-than-32-bytes");
            Environment.SetEnvironmentVariable("SecretProtection__MasterKey", "integration-test-secret-protection-key-that-is-longer-than-32-bytes");
            Environment.SetEnvironmentVariable("SearchBackend__Vector", "qdrant");
            Environment.SetEnvironmentVariable("SearchBackend__Lexical", "bm25");
            Environment.SetEnvironmentVariable("SearchBackend__AutoInitializeNativeIndexes", "false");
            Environment.SetEnvironmentVariable("Storage__Provider", "local");
            Environment.SetEnvironmentVariable("Storage__RootPath", _rootPath);
            Environment.SetEnvironmentVariable("OpenTelemetry__Enabled", "false");
            Environment.SetEnvironmentVariable("RateLimiting__PermitLimit", permitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Environment.SetEnvironmentVariable("RateLimiting__AuthPermitLimit", authPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Environment.SetEnvironmentVariable("RateLimiting__WindowSeconds", "60");
            Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", _allowedOrigin);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "Sqlite",
                    ["Database:ConnectionString"] = $"Data Source={Path.Combine(_rootPath, "knowledge.db")}",
                    ["Database:AutoMigrate"] = "true",
                    ["Auth:SigningKey"] = "integration-test-signing-key-that-is-longer-than-32-bytes",
                    ["SecretProtection:MasterKey"] = "integration-test-secret-protection-key-that-is-longer-than-32-bytes",
                    ["SearchBackend:Vector"] = "qdrant",
                    ["SearchBackend:Lexical"] = "bm25",
                    ["SearchBackend:AutoInitializeNativeIndexes"] = "false",
                    ["Storage:Provider"] = "local",
                    ["Storage:RootPath"] = _rootPath,
                    ["OpenTelemetry:Enabled"] = "false",
                    ["RateLimiting:PermitLimit"] = _permitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["RateLimiting:AuthPermitLimit"] = _authPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["RateLimiting:WindowSeconds"] = "60",
                    ["Cors:AllowedOrigins:0"] = _allowedOrigin
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
            Environment.SetEnvironmentVariable("Auth__SigningKey", null);
            Environment.SetEnvironmentVariable("SecretProtection__MasterKey", null);
            Environment.SetEnvironmentVariable("SearchBackend__Vector", null);
            Environment.SetEnvironmentVariable("SearchBackend__Lexical", null);
            Environment.SetEnvironmentVariable("SearchBackend__AutoInitializeNativeIndexes", null);
            Environment.SetEnvironmentVariable("Storage__Provider", null);
            Environment.SetEnvironmentVariable("Storage__RootPath", null);
            Environment.SetEnvironmentVariable("OpenTelemetry__Enabled", null);
            Environment.SetEnvironmentVariable("RateLimiting__PermitLimit", null);
            Environment.SetEnvironmentVariable("RateLimiting__AuthPermitLimit", null);
            Environment.SetEnvironmentVariable("RateLimiting__WindowSeconds", null);
            Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", null);

            if (Directory.Exists(_rootPath))
                Directory.Delete(_rootPath, recursive: true);
        }
    }
}
