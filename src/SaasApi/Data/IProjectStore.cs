namespace SaasApi.Data;

/// <summary>
/// Low-level storage. Every call takes the container AND the tenant id, and implementations must use the
/// tenant id as the partition key on every read and write. Application code never calls this directly:
/// it goes through <see cref="TenantScopedProjectRepository"/>, which fills both in from the request.
/// </summary>
public interface IProjectStore
{
    Task<Project?> GetAsync(string container, string tenantId, string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Project>> ListAsync(string container, string tenantId, CancellationToken cancellationToken);
    Task<int> CountAsync(string container, string tenantId, CancellationToken cancellationToken);
    Task CreateAsync(string container, Project project, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(string container, string tenantId, string id, CancellationToken cancellationToken);
}
