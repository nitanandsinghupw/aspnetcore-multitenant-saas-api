namespace SaasApi.Tenancy;

public enum TenantTier { Free, Pro, Enterprise }

/// <summary>
/// Pooled: shares a container with other tenants, isolated by partition key.
/// Dedicated: gets its own container (stronger isolation, own throughput, easy per-tenant export/delete).
/// </summary>
public enum IsolationMode { Pooled, Dedicated }

public sealed record Tenant(string Id, string Name, TenantTier Tier, IsolationMode Isolation, bool Suspended = false);

/// <summary>What each tier gets. Enforced by the rate limiter and the repository.</summary>
public sealed record TierLimits(int BurstRequests, int RequestsPerSecond, int? MaxProjects)
{
    public static TierLimits For(TenantTier tier) => tier switch
    {
        TenantTier.Free => new TierLimits(BurstRequests: 10, RequestsPerSecond: 1, MaxProjects: 3),
        TenantTier.Pro => new TierLimits(BurstRequests: 100, RequestsPerSecond: 10, MaxProjects: 50),
        TenantTier.Enterprise => new TierLimits(BurstRequests: 500, RequestsPerSecond: 50, MaxProjects: null),
        _ => throw new ArgumentOutOfRangeException(nameof(tier))
    };
}
