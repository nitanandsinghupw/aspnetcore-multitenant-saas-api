using SaasApi.Data;
using SaasApi.Tenancy;

namespace SaasApi.Tests;

public class TenantIsolationTests
{
    private static readonly Tenant Acme = new("acme", "Acme", TenantTier.Free, IsolationMode.Pooled);
    private static readonly Tenant Globex = new("globex", "Globex", TenantTier.Pro, IsolationMode.Pooled);
    private static readonly Tenant Initech = new("initech", "Initech", TenantTier.Enterprise, IsolationMode.Dedicated);

    private static TenantScopedProjectRepository RepoFor(Tenant tenant, IProjectStore store)
    {
        var context = new TenantContext();
        context.Set(tenant);
        return new TenantScopedProjectRepository(store, context, new ContainerResolver());
    }

    [Fact]
    public async Task A_tenant_cannot_read_another_tenants_project_even_with_its_id()
    {
        var store = new InMemoryProjectStore();
        Project acmeProject = await RepoFor(Acme, store).CreateAsync("Secret plans", null, "alice", CancellationToken.None);

        Project? seenByGlobex = await RepoFor(Globex, store).GetAsync(acmeProject.Id, CancellationToken.None);

        Assert.Null(seenByGlobex);
    }

    [Fact]
    public async Task A_tenant_cannot_delete_another_tenants_project()
    {
        var store = new InMemoryProjectStore();
        Project acmeProject = await RepoFor(Acme, store).CreateAsync("Keep me", null, "alice", CancellationToken.None);

        bool deleted = await RepoFor(Globex, store).DeleteAsync(acmeProject.Id, CancellationToken.None);

        Assert.False(deleted);
        Assert.NotNull(await RepoFor(Acme, store).GetAsync(acmeProject.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Listing_returns_only_the_callers_projects()
    {
        var store = new InMemoryProjectStore();
        await RepoFor(Acme, store).CreateAsync("A1", null, "alice", CancellationToken.None);
        await RepoFor(Globex, store).CreateAsync("G1", null, "bob", CancellationToken.None);
        await RepoFor(Globex, store).CreateAsync("G2", null, "bob", CancellationToken.None);

        IReadOnlyList<Project> globexProjects = await RepoFor(Globex, store).ListAsync(CancellationToken.None);

        Assert.Equal(2, globexProjects.Count);
        Assert.All(globexProjects, p => Assert.Equal("globex", p.TenantId));
    }

    [Fact]
    public async Task New_projects_always_get_the_tenant_from_the_request_context()
    {
        var store = new InMemoryProjectStore();

        Project project = await RepoFor(Globex, store).CreateAsync("G1", null, "bob", CancellationToken.None);

        Assert.Equal("globex", project.TenantId);
    }

    [Fact]
    public async Task Dedicated_tenants_are_stored_in_their_own_container()
    {
        var store = new InMemoryProjectStore();
        await RepoFor(Initech, store).CreateAsync("ERP", null, "carol", CancellationToken.None);
        await RepoFor(Acme, store).CreateAsync("Site", null, "alice", CancellationToken.None);

        Assert.Contains("projects-initech", store.ContainerNames);
        Assert.Contains("projects", store.ContainerNames);
    }

    [Fact]
    public async Task Free_plan_is_limited_to_three_projects()
    {
        var store = new InMemoryProjectStore();
        TenantScopedProjectRepository repo = RepoFor(Acme, store);
        for (int i = 0; i < 3; i++) await repo.CreateAsync($"P{i}", null, "alice", CancellationToken.None);

        await Assert.ThrowsAsync<QuotaExceededException>(() => repo.CreateAsync("P4", null, "alice", CancellationToken.None));
    }

    [Fact]
    public async Task One_tenants_usage_does_not_count_against_another()
    {
        var store = new InMemoryProjectStore();
        var other = new Tenant("other", "Other", TenantTier.Free, IsolationMode.Pooled);
        for (int i = 0; i < 3; i++) await RepoFor(Acme, store).CreateAsync($"A{i}", null, "alice", CancellationToken.None);

        Project created = await RepoFor(other, store).CreateAsync("First", null, "dave", CancellationToken.None);

        Assert.Equal("other", created.TenantId);
    }

    [Fact]
    public async Task Enterprise_plan_has_no_project_limit()
    {
        var store = new InMemoryProjectStore();
        TenantScopedProjectRepository repo = RepoFor(Initech, store);

        for (int i = 0; i < 60; i++) await repo.CreateAsync($"P{i}", null, "carol", CancellationToken.None);

        Assert.Equal(60, (await repo.ListAsync(CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Blank_names_are_rejected()
    {
        TenantScopedProjectRepository repo = RepoFor(Globex, new InMemoryProjectStore());

        await Assert.ThrowsAsync<ArgumentException>(() => repo.CreateAsync("  ", null, "bob", CancellationToken.None));
    }
}
