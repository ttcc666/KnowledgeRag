using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeRag.Infrastructure.Messaging;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMessagingModule(this IServiceCollection services, IConfiguration configuration)
    {
        var database = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        var integration = configuration.GetSection(CapIntegrationOptions.SectionName).Get<CapIntegrationOptions>() ?? new CapIntegrationOptions();
        services.Configure<CapIntegrationOptions>(configuration.GetSection(CapIntegrationOptions.SectionName));

        services.AddCap(options =>
        {
            if (database.Provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
            {
                options.UsePostgreSql(postgres =>
                {
                    postgres.ConnectionString = database.ConnectionString;
                    postgres.Schema = "cap";
                });
                options.UseStorageLock = true;
            }
            else
            {
                options.UseInMemoryStorage();
            }

            if (integration.Transport.Equals("rabbitmq", StringComparison.OrdinalIgnoreCase))
            {
                options.UseRabbitMQ(rabbit =>
                {
                    rabbit.HostName = integration.RabbitMq.HostName;
                    rabbit.Port = integration.RabbitMq.Port;
                    rabbit.UserName = integration.RabbitMq.UserName;
                    rabbit.Password = integration.RabbitMq.Password;
                    rabbit.VirtualHost = integration.RabbitMq.VirtualHost;
                    rabbit.ExchangeName = integration.RabbitMq.ExchangeName;
                });
            }
            else
            {
                options.UseInMemoryMessageQueue();
            }

            options.FailedRetryCount = Math.Clamp(integration.FailedRetryCount, 1, 100);
        });

        services.AddTransient<DocumentIndexingSubscriber>();
        return services;
    }
}
