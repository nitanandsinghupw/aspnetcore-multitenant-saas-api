using System.Collections.Concurrent;

namespace SaasApi.Tenancy;

/// <summary>The list of tenants. In production this is a small "catalog" database or container.</summary>
public interface ITenantCatalog
{
    Tenant? Find(string tenantId);
}

public sealed class InMemoryTenantCatalog : ITenantCatalog
{
    private readonly ConcurrentDictionary<string, Tenant> _tenants;

    public InMemoryTenantCatalog(IEnumerable<Tenant> tenants) =>
        _tenants = new ConcurrentDictionary<string, Tenant>(tenants.ToDictionary(t => t.Id, StringComparer.Ordinal));

    public Tenant? Find(string tenantId) => _tenants.TryGetValue(tenantId, out Tenant? t) ? t : null;
}
