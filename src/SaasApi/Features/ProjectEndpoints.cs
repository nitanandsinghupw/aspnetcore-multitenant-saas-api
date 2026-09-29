using System.Security.Claims;
using SaasApi.Data;
using SaasApi.Security;
using SaasApi.Tenancy;

namespace SaasApi.Features;

public sealed record CreateProjectRequest(string Name, string? Description);

public static class ProjectEndpoints
{
    public static void MapSaasEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" })).DisableRateLimiting();

        app.MapGet("/me", (ITenantContext tenant, ClaimsPrincipal user) =>
        {
            Tenant t = tenant.Current;
            TierLimits limits = TierLimits.For(t.Tier);
            return Results.Ok(new
            {
                tenant = new { t.Id, t.Name, tier = t.Tier.ToString(), isolation = t.Isolation.ToString() },
                caller = new { name = user.Identity?.Name, role = user.FindFirst(ClaimTypes.Role)?.Value },
                limits = new { limits.BurstRequests, limits.RequestsPerSecond, limits.MaxProjects }
            });
        }).RequireAuthorization(SaasPolicies.CanRead);

        RouteGroupBuilder projects = app.MapGroup("/projects").RequireAuthorization(SaasPolicies.CanRead);

        projects.MapGet("/", async (TenantScopedProjectRepository repo, CancellationToken ct) =>
            Results.Ok(await repo.ListAsync(ct)));

        // 404, not 403, for another tenant's id: don't even confirm that it exists.
        projects.MapGet("/{id}", async (string id, TenantScopedProjectRepository repo, CancellationToken ct) =>
            await repo.GetAsync(id, ct) is { } project ? Results.Ok(project) : Results.NotFound());

        projects.MapPost("/", async (CreateProjectRequest request, TenantScopedProjectRepository repo, ClaimsPrincipal user, CancellationToken ct) =>
        {
            try
            {
                Project project = await repo.CreateAsync(request.Name, request.Description, user.Identity?.Name ?? "unknown", ct);
                return Results.Created($"/projects/{project.Id}", project);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (QuotaExceededException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden, title: "Plan limit reached");
            }
        }).RequireAuthorization(SaasPolicies.CanWrite);

        projects.MapDelete("/{id}", async (string id, TenantScopedProjectRepository repo, CancellationToken ct) =>
            await repo.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(SaasPolicies.CanAdmin);
    }
}
