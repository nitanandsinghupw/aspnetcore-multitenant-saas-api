using SaasApi.Tenancy;

namespace SaasApi.Tests;

public class TenantContextTests
{
    [Fact]
    public void Using_tenant_data_without_a_resolved_tenant_fails_loudly()
    {
        var context = new TenantContext();

        Assert.False(context.IsResolved);
        Assert.Throws<InvalidOperationException>(() => { _ = context.Current; });
    }

    [Fact]
    public void The_tenant_cannot_be_switched_during_a_request()
    {
        var context = new TenantContext();
        context.Set(new Tenant("acme", "Acme", TenantTier.Free, IsolationMode.Pooled));

        Assert.Throws<InvalidOperationException>(() =>
            context.Set(new Tenant("globex", "Globex", TenantTier.Pro, IsolationMode.Pooled)));
    }

    [Fact]
    public void Higher_tiers_get_higher_limits()
    {
        Assert.True(TierLimits.For(TenantTier.Pro).RequestsPerSecond > TierLimits.For(TenantTier.Free).RequestsPerSecond);
        Assert.True(TierLimits.For(TenantTier.Enterprise).BurstRequests > TierLimits.For(TenantTier.Pro).BurstRequests);
        Assert.Null(TierLimits.For(TenantTier.Enterprise).MaxProjects);
    }
}
