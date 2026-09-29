using System.Collections.Concurrent;

namespace SaasApi.Data;

/// <summary>
/// In-memory store shaped like Cosmos DB: containers hold items addressed by (partition key, id).
/// A lookup with the wrong partition key finds nothing, exactly as a Cosmos point read would.
/// </summary>
public sealed class InMemoryProjectStore : IProjectStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<(string TenantId, string Id), Project>> _containers = new();

    private ConcurrentDictionary<(string, string), Project> Container(string name) =>
        _containers.GetOrAdd(name, _ => new ConcurrentDictionary<(string, string), Project>());

    public IReadOnlyCollection<string> ContainerNames => _containers.Keys.ToList();

    public Task<Project?> GetAsync(string container, string tenantId, string id, CancellationToken cancellationToken) =>
        Task.FromResult(Container(container).TryGetValue((tenantId, id), out Project? p) ? p : null);

    public Task<IReadOnlyList<Project>> ListAsync(string container, string tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Project>>(Container(container).Values
            .Where(p => p.TenantId == tenantId).OrderBy(p => p.CreatedAt).ToList());

    public Task<int> CountAsync(string container, string tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(Container(container).Values.Count(p => p.TenantId == tenantId));

    public Task CreateAsync(string container, Project project, CancellationToken cancellationToken)
    {
        if (!Container(container).TryAdd((project.TenantId, project.Id), project))
            throw new InvalidOperationException($"Project {project.Id} already exists.");
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string container, string tenantId, string id, CancellationToken cancellationToken) =>
        Task.FromResult(Container(container).TryRemove((tenantId, id), out _));
}
