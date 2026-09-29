using SaasApi.Tenancy;

namespace SaasApi.Data;

public sealed class QuotaExceededException : Exception
{
    public QuotaExceededException(string message) : base(message) { }
}

/// <summary>
/// The only data access the endpoints use. No method accepts a tenant id: it always comes from the
/// resolved tenant for the request. So a request for another tenant's project id simply finds nothing,
/// and there is no code path where a forgotten filter leaks data across tenants.
/// </summary>
public sealed class TenantScopedProjectRepository
{
    private readonly IProjectStore _store;
    private readonly ITenantContext _tenant;
    private readonly ContainerResolver _containers;
    private readonly Func<DateTimeOffset> _now;

    public TenantScopedProjectRepository(IProjectStore store, ITenantContext tenant, ContainerResolver containers, Func<DateTimeOffset>? now = null)
    {
        _store = store;
        _tenant = tenant;
        _containers = containers;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    private Tenant Tenant => _tenant.Current;
    private string Container => _containers.For(Tenant);

    public Task<Project?> GetAsync(string id, CancellationToken ct) => _store.GetAsync(Container, Tenant.Id, id, ct);

    public Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct) => _store.ListAsync(Container, Tenant.Id, ct);

    public async Task<Project> CreateAsync(string name, string? description, string createdBy, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (name.Length > 100) throw new ArgumentException("Name must be 100 characters or fewer.", nameof(name));

        // Note: count-then-insert can race by one under concurrency. Fine for a soft limit;
        // for a hard limit, keep a per-tenant counter document and update it with an ETag check.
        int? max = TierLimits.For(Tenant.Tier).MaxProjects;
        if (max is int limit && await _store.CountAsync(Container, Tenant.Id, ct) >= limit)
            throw new QuotaExceededException($"The {Tenant.Tier} plan allows {limit} projects. Upgrade to add more.");

        var project = new Project(Guid.NewGuid().ToString("N"), Tenant.Id, name.Trim(), description?.Trim(), _now(), createdBy);
        await _store.CreateAsync(Container, project, ct);
        return project;
    }

    public Task<bool> DeleteAsync(string id, CancellationToken ct) => _store.DeleteAsync(Container, Tenant.Id, id, ct);
}
