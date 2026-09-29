using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasApi.Data;

namespace SaasApi.Cosmos;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Replaces the in-memory store with Cosmos DB, authenticating with managed identity
    /// (DefaultAzureCredential): no keys or connection strings in configuration.
    /// </summary>
    public static IServiceCollection AddCosmosProjectStore(this IServiceCollection services, string accountEndpoint, string databaseName)
    {
        services.AddSingleton(_ => new CosmosClient(accountEndpoint, new DefaultAzureCredential(), new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase },
            ApplicationName = "saas-api"
        }));
        services.RemoveAll<IProjectStore>();
        services.AddSingleton<IProjectStore>(sp => new CosmosProjectStore(sp.GetRequiredService<CosmosClient>(), databaseName));
        return services;
    }
}
