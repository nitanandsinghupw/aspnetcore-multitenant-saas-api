using SaasApi.Security;

namespace SaasApi.Tenancy;

/// <summary>
/// Runs after authentication. Reads the tenant id from the caller's verified claims, loads the tenant,
/// rejects unknown or suspended tenants, and adds TenantId to every log line for this request.
/// </summary>
public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext http, TenantContext tenantContext, ITenantCatalog catalog)
    {
        if (http.User.Identity?.IsAuthenticated != true)
        {
            await _next(http);   // anonymous endpoints (health); tenant endpoints require auth anyway
            return;
        }

        string? tenantId = http.User.FindFirst(SaasClaims.TenantId)?.Value;
        Tenant? tenant = tenantId is null ? null : catalog.Find(tenantId);

        if (tenant is null)
        {
            await Results.Problem("The credential is not linked to a known tenant.", statusCode: StatusCodes.Status403Forbidden)
                .ExecuteAsync(http);
            return;
        }

        if (tenant.Suspended)
        {
            await Results.Problem($"Tenant '{tenant.Id}' is suspended.", statusCode: StatusCodes.Status403Forbidden)
                .ExecuteAsync(http);
            return;
        }

        tenantContext.Set(tenant);
        using (_logger.BeginScope(new Dictionary<string, object> { ["TenantId"] = tenant.Id, ["TenantTier"] = tenant.Tier.ToString() }))
        {
            await _next(http);
        }
    }
}
