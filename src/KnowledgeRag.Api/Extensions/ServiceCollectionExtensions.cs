using System.Text.Json.Serialization;
using KnowledgeRag.Api.Realtime;
using KnowledgeRag.Application.Abstractions;

namespace KnowledgeRag.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddApiAuthentication(configuration);
        services.AddApiSwagger();
        services.AddApiTelemetry(configuration);
        services.AddSignalR().AddJsonProtocol(options =>
            options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddSingleton<IDocumentProcessingNotifier, SignalRDocumentProcessingNotifier>();
        return services;
    }
}
