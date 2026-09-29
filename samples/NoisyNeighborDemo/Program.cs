using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using SaasApi;

// Acme (Free plan) floods the API with 20 parallel clients for 4 seconds.
// Globex (Pro plan) makes one normal request every 100 ms during the same period.
// Run once with a single shared limit and once with a limit per tenant.
TimeSpan duration = TimeSpan.FromSeconds(4);

Console.WriteLine("Noisy neighbour demo: Acme (Free) floods the API while Globex (Pro) works normally.");
Console.WriteLine();
Console.WriteLine($"{"Rate limiting",-22} {"Acme sent",10} {"Acme 429s",10} {"Globex sent",12} {"Globex OK",10} {"Globex 429s",12} {"Globex success",15}");
Console.WriteLine(new string('-', 97));

await RunAsync("One shared limit", mode: "Shared");
await RunAsync("Per-tenant limits", mode: "PerTenant");

Console.WriteLine();
Console.WriteLine("With per-tenant limits, only the tenant that exceeds its own plan is throttled.");

async Task RunAsync(string label, string mode)
{
    WebApplication app = SaasApiApp.Create(new WebApplicationOptions { EnvironmentName = "Development" }, builder =>
    {
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["Seed:UseDevData"] = "true";
        builder.Configuration["RateLimiting:Mode"] = mode;
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
    });
    await app.StartAsync();
    var baseAddress = new Uri(app.Urls.First());

    using var acme = Client(baseAddress, DevSeed.Keys.AcmeAdmin);
    using var globex = Client(baseAddress, DevSeed.Keys.GlobexMember);
    using var stop = new CancellationTokenSource(duration);

    var acmeStats = new Stats();
    var globexStats = new Stats();

    Task[] flood = Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
    {
        while (!stop.IsCancellationRequested) await Send(acme, acmeStats);
    })).ToArray();

    Task normal = Task.Run(async () =>
    {
        while (!stop.IsCancellationRequested)
        {
            await Send(globex, globexStats);
            try { await Task.Delay(100, stop.Token); } catch (OperationCanceledException) { }
        }
    });

    await Task.WhenAll(flood.Append(normal));
    await app.StopAsync();
    await app.DisposeAsync();

    double success = globexStats.Sent == 0 ? 0 : (double)globexStats.Ok / globexStats.Sent;
    Console.WriteLine($"{label,-22} {acmeStats.Sent,10:N0} {acmeStats.Throttled,10:N0} {globexStats.Sent,12:N0} {globexStats.Ok,10:N0} {globexStats.Throttled,12:N0} {success * 100,14:F0}%");
}

static async Task Send(HttpClient client, Stats stats)
{
    try
    {
        using HttpResponseMessage response = await client.GetAsync("/projects");
        Interlocked.Increment(ref stats.Sent);
        if (response.StatusCode == HttpStatusCode.OK) Interlocked.Increment(ref stats.Ok);
        else if (response.StatusCode == HttpStatusCode.TooManyRequests) Interlocked.Increment(ref stats.Throttled);
    }
    catch (HttpRequestException)
    {
        // server stopping at the end of the run
    }
}

static HttpClient Client(Uri baseAddress, string apiKey)
{
    var client = new HttpClient { BaseAddress = baseAddress };
    client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    return client;
}

sealed class Stats
{
    public int Sent;
    public int Ok;
    public int Throttled;
}
