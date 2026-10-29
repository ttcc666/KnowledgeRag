using System.Globalization;
using System.Text.Json.Serialization;
using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using KnowledgeRag.Api.Realtime;
using KnowledgeRag.Application.Abstractions;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace KnowledgeRag.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        SecurityConfigurationValidator.Validate(configuration, environment.IsProduction());
        services.AddApiAuthentication(configuration);
        services.AddApiSwagger();
        services.AddApiTelemetry(configuration);
        services.AddSignalR().AddJsonProtocol(options =>
            options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddSingleton<IDocumentProcessingNotifier, SignalRDocumentProcessingNotifier>();

        var allowedOrigins = (configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        services.AddCors(options => options.AddPolicy("ConfiguredOrigins", policy =>
        {
            if (allowedOrigins.Length > 0)
                policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
        }));

        var permitLimit = configuration.GetValue("RateLimiting:PermitLimit", 120);
        var authPermitLimit = configuration.GetValue("RateLimiting:AuthPermitLimit", 10);
        var windowSeconds = configuration.GetValue("RateLimiting:WindowSeconds", 60);
        if (permitLimit is < 1 or > 100_000)
            throw new InvalidOperationException("RateLimiting:PermitLimit 必须在 1 到 100000 之间。");
        if (authPermitLimit is < 1 or > 10_000)
            throw new InvalidOperationException("RateLimiting:AuthPermitLimit 必须在 1 到 10000 之间。");
        if (windowSeconds is < 1 or > 3600)
            throw new InvalidOperationException("RateLimiting:WindowSeconds 必须在 1 到 3600 之间。");

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var requestPath = (context.Request.Path.Value ?? string.Empty).TrimEnd('/');
                var isSensitiveAuthEndpoint =
                    string.Equals(requestPath, "/api/auth/login", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(requestPath, "/api/auth/register", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(requestPath, "/api/auth/refresh", StringComparison.OrdinalIgnoreCase);
                var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                string partitionKey;
                if (isSensitiveAuthEndpoint)
                {
                    // Keep unauthenticated auth attempts isolated from ordinary API traffic and
                    // from each other by endpoint, while using the source IP as the stable identity.
                    partitionKey = $"auth:{requestPath}:{remoteIp}";
                }
                else
                {
                    var principal = context.User;
                    var apiKeyId = principal.FindFirst("kr:api_key_id")?.Value;
                    var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    partitionKey = !string.IsNullOrWhiteSpace(apiKeyId)
                        ? $"api-key:{apiKeyId}"
                        : principal.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(userId)
                            ? $"user:{userId}"
                            : $"ip:{remoteIp}";
                }

                var effectivePermitLimit = isSensitiveAuthEndpoint
                    ? Math.Min(permitLimit, authPermitLimit)
                    : permitLimit;
                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = effectivePermitLimit,
                    Window = TimeSpan.FromSeconds(windowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
            });
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = windowSeconds.ToString(CultureInfo.InvariantCulture);
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { error = "请求过于频繁，请稍后重试。" },
                    cancellationToken);
            };
        });

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            foreach (var value in configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (!IPAddress.TryParse(value, out var address))
                    throw new InvalidOperationException($"ForwardedHeaders:KnownProxies 包含无效 IP 地址：{value}");
                if (!options.KnownProxies.Contains(address)) options.KnownProxies.Add(address);
            }
        });

        var hstsMaxAgeDays = configuration.GetValue("Security:HstsMaxAgeDays", 180);
        if (hstsMaxAgeDays is < 1 or > 730)
            throw new InvalidOperationException("Security:HstsMaxAgeDays 必须在 1 到 730 之间。");
        services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(hstsMaxAgeDays);
            options.IncludeSubDomains = configuration.GetValue("Security:HstsIncludeSubDomains", false);
        });

        return services;
    }
}
