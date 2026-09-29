using SaasApi.Tenancy;

namespace SaasApi.Data;

/// <summary>Picks where a tenant's data lives: the shared pooled container, or the tenant's own.</summary>
public sealed class ContainerResolver
{
    public const string PooledContainer = "projects";

    public string For(Tenant tenant) => tenant.Isolation switch
    {
        IsolationMode.Pooled => PooledContainer,
        IsolationMode.Dedicated => $"projects-{tenant.Id}",
        _ => throw new ArgumentOutOfRangeException(nameof(tenant))
    };
}
