using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace KnowledgeRag.Api.Extensions;

internal static class TelemetryExtensions
{
    public static IServiceCollection AddApiTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        var otel = configuration.GetSection(OpenTelemetryOptions.SectionName).Get<OpenTelemetryOptions>() ?? new OpenTelemetryOptions();
        if (!otel.Enabled) return services;

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(otel.ServiceName))
            .WithTracing(tracing =>
            {
                tracing.AddSource(Telemetry.ActivitySourceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
                if (!string.IsNullOrWhiteSpace(otel.OtlpEndpoint)) tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otel.OtlpEndpoint));
                if (otel.ConsoleExporter) tracing.AddConsoleExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation();
                if (!string.IsNullOrWhiteSpace(otel.OtlpEndpoint)) metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otel.OtlpEndpoint));
                if (otel.ConsoleExporter) metrics.AddConsoleExporter();
            });
        return services;
    }
}
