using System.Net;
using Microsoft.Azure.Cosmos;
using SaasApi.Data;

namespace SaasApi.Cosmos;

/// <summary>
/// Cosmos DB implementation of <see cref="IProjectStore"/>. Every operation passes the tenant id as the
/// partition key, so reads and queries physically cannot touch another tenant's items, and every query
/// is a cheap single-partition query instead of a cross-partition fan-out.
///
/// Dedicated tenant containers are created during tenant onboarding (see infra/main.bicep), not here:
/// the app's managed identity only has data-plane access and cannot create containers.
/// </summary>
public sealed class CosmosProjectStore : IProjectStore
{
    private static readonly ItemRequestOptions NoContentResponse = new() { EnableContentResponseOnWrite = false };
    private readonly CosmosClient _client;
    private readonly string _databaseName;

    public CosmosProjectStore(CosmosClient client, string databaseName)
    {
        _client = client;
        _databaseName = databaseName;
    }

    private Container Container(string name) => _client.GetContainer(_databaseName, name);

    public async Task<Project?> GetAsync(string container, string tenantId, string id, CancellationToken cancellationToken)
    {
        try
        {
            ItemResponse<Project> response = await Container(container)
                .ReadItemAsync<Project>(id, new PartitionKey(tenantId), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<Project>> ListAsync(string container, string tenantId, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.tenantId = @tenantId ORDER BY c.createdAt")
            .WithParameter("@tenantId", tenantId);
        var results = new List<Project>();
        using FeedIterator<Project> iterator = Container(container).GetItemQueryIterator<Project>(query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenantId) });
        while (iterator.HasMoreResults)
            results.AddRange(await iterator.ReadNextAsync(cancellationToken));
        return results;
    }

    public async Task<int> CountAsync(string container, string tenantId, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.tenantId = @tenantId")
            .WithParameter("@tenantId", tenantId);
        using FeedIterator<int> iterator = Container(container).GetItemQueryIterator<int>(query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenantId) });
        int count = 0;
        while (iterator.HasMoreResults)
            foreach (int value in await iterator.ReadNextAsync(cancellationToken)) count += value;
        return count;
    }

    public async Task CreateAsync(string container, Project project, CancellationToken cancellationToken)
    {
        try
        {
            await Container(container).CreateItemAsync(project, new PartitionKey(project.TenantId), NoContentResponse, cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException($"Project {project.Id} already exists.", ex);
        }
    }

    public async Task<bool> DeleteAsync(string container, string tenantId, string id, CancellationToken cancellationToken)
    {
        try
        {
            await Container(container).DeleteItemAsync<Project>(id, new PartitionKey(tenantId), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
