namespace SaasApi.Security;

public static class SaasClaims
{
    public const string TenantId = "tenant_id";
    public const string KeyId = "key_id";
}

public static class SaasRoles
{
    public const string Admin = "admin";
    public const string Member = "member";
    public const string Reader = "reader";
}

public static class SaasPolicies
{
    public const string CanRead = nameof(CanRead);
    public const string CanWrite = nameof(CanWrite);
    public const string CanAdmin = nameof(CanAdmin);
}
