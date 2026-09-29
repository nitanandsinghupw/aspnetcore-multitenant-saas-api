namespace SaasApi.Tenancy;

/// <summary>
/// The tenant for the current request. Set once by <see cref="TenantResolutionMiddleware"/> from the
/// authenticated identity, never from a header, query string or request body the client controls.
/// </summary>
public interface ITenantContext
{
    Tenant Current { get; }
    bool IsResolved { get; }
}

public sealed class TenantContext : ITenantContext
{
    private Tenant? _tenant;

    public Tenant Current => _tenant ?? throw new InvalidOperationException(
        "No tenant resolved for this request. Tenant-scoped services can only be used on authenticated endpoints.");

    public bool IsResolved => _tenant is not null;

    public void Set(Tenant tenant)
    {
        if (_tenant is not null && _tenant.Id != tenant.Id)
            throw new InvalidOperationException("The tenant for a request cannot change once set.");
        _tenant = tenant;
    }
}
