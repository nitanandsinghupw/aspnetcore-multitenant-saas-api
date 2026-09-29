using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasApi.Data;
using SaasApi.Features;
using SaasApi.Security;
using SaasApi.Tenancy;

namespace SaasApi;

public static class SaasApiApp
{
    public static WebApplication Create(WebApplicationOptions options, Action<WebApplicationBuilder>? configure = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(options);
        configure?.Invoke(builder);

        bool useDevData = builder.Configuration.GetValue<bool>("Seed:UseDevData");
        if (useDevData && builder.Environment.IsProduction())
            throw new InvalidOperationException("Development seed data must never be enabled in Production.");

        // Tenants and credentials. TryAdd lets a host register real implementations first
        // (a catalog database, a key store, Cosmos DB) in the configure callback.
        builder.Services.TryAddSingleton<ITenantCatalog>(new InMemoryTenantCatalog(useDevData ? DevSeed.Tenants : Array.Empty<Tenant>()));
        builder.Services.TryAddSingleton<IApiKeyStore>(new InMemoryApiKeyStore(useDevData ? DevSeed.ApiKeys : Array.Empty<ApiKeyRecord>()));

        // Per-request tenant, and data access that can only see that tenant
        builder.Services.AddScoped<TenantContext>();
        builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        builder.Services.AddSingleton<ContainerResolver>();
        builder.Services.TryAddSingleton<IProjectStore, InMemoryProjectStore>();
        builder.Services.AddScoped(sp => new TenantScopedProjectRepository(
            sp.GetRequiredService<IProjectStore>(), sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<ContainerResolver>()));

        builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(SaasPolicies.CanRead, p => p.RequireAuthenticatedUser().RequireClaim(SaasClaims.TenantId))
            .AddPolicy(SaasPolicies.CanWrite, p => p.RequireAuthenticatedUser().RequireRole(SaasRoles.Admin, SaasRoles.Member))
            .AddPolicy(SaasPolicies.CanAdmin, p => p.RequireAuthenticatedUser().RequireRole(SaasRoles.Admin));

        bool sharedLimiter = string.Equals(builder.Configuration["RateLimiting:Mode"], "Shared", StringComparison.OrdinalIgnoreCase);

        // Noisy-neighbour protection: every tenant gets its own token bucket, sized by its plan.
        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                TenantContext tenant = http.RequestServices.GetRequiredService<TenantContext>();
                if (!tenant.IsResolved)
                {
                    string ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    return RateLimitPartition.GetFixedWindowLimiter($"anon:{ip}", _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    });
                }

                TierLimits limits = TierLimits.For(tenant.Current.Tier);

                // "Shared" exists only so the NoisyNeighborDemo can show what goes wrong without per-tenant limits.
                if (sharedLimiter)
                    return RateLimitPartition.GetTokenBucketLimiter("all-tenants", _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 110,
                        TokensPerPeriod = 11,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });

                return RateLimitPartition.GetTokenBucketLimiter($"tenant:{tenant.Current.Id}", _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = limits.BurstRequests,
                    TokensPerPeriod = limits.RequestsPerSecond,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
            });
            limiter.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                await Results.Problem("Rate limit for your plan exceeded. Retry after the time in the Retry-After header.",
                    statusCode: StatusCodes.Status429TooManyRequests).ExecuteAsync(context.HttpContext);
            };
        });

        builder.Services.AddProblemDetails();

        WebApplication app = builder.Build();

        app.UseAuthentication();
        app.UseMiddleware<TenantResolutionMiddleware>();   // tenant must be known before rate limiting
        app.UseRateLimiter();
        app.UseAuthorization();
        app.MapSaasEndpoints();

        return app;
    }
}
