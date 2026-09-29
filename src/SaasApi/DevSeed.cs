using SaasApi.Security;
using SaasApi.Tenancy;

namespace SaasApi;

/// <summary>
/// Development-only tenants and API keys, loaded when "Seed:UseDevData" is true
/// (set in appsettings.Development.json). Never enable this in production.
/// </summary>
public static class DevSeed
{
    public static class Keys
    {
        public const string AcmeAdmin = "sk_acme01_dev-acme-admin-000000000001";
        public const string AcmeReader = "sk_acme02_dev-acme-reader-00000000002";
        public const string GlobexMember = "sk_globex1_dev-globex-member-0000003";
        public const string InitechAdmin = "sk_initech1_dev-initech-admin-000004";
        public const string UmbrellaAdmin = "sk_umbrella1_dev-umbrella-admin-0005";
    }

    public static IReadOnlyList<Tenant> Tenants { get; } = new[]
    {
        new Tenant("acme", "Acme Corp", TenantTier.Free, IsolationMode.Pooled),
        new Tenant("globex", "Globex", TenantTier.Pro, IsolationMode.Pooled),
        new Tenant("initech", "Initech", TenantTier.Enterprise, IsolationMode.Dedicated),
        new Tenant("umbrella", "Umbrella", TenantTier.Pro, IsolationMode.Pooled, Suspended: true)
    };

    public static IReadOnlyList<ApiKeyRecord> ApiKeys { get; } = new[]
    {
        Record(Keys.AcmeAdmin, "acme", SaasRoles.Admin, "acme-admin"),
        Record(Keys.AcmeReader, "acme", SaasRoles.Reader, "acme-reporting"),
        Record(Keys.GlobexMember, "globex", SaasRoles.Member, "globex-ci"),
        Record(Keys.InitechAdmin, "initech", SaasRoles.Admin, "initech-admin"),
        Record(Keys.UmbrellaAdmin, "umbrella", SaasRoles.Admin, "umbrella-admin")
    };

    private static ApiKeyRecord Record(string plaintext, string tenantId, string role, string name)
    {
        ApiKey.TryParse(plaintext, out string keyId, out string secret);
        return new ApiKeyRecord(keyId, ApiKey.Hash(secret), tenantId, role, name);
    }
}
