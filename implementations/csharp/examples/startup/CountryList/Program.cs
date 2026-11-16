using System.Net.Http.Json;
using System.Text.Json;
using CIS.Healthcheck;
using CIS.Healthcheck.AspNetCore;
using CIS.Heartbeat;
using CIS.Heartbeat.Hosting;
using CIS.Startup;
using CIS.Startup.Hosting;
using CIS.TypeManagement;

namespace CIS.Startup.Examples.CountryList;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        bool selfTest = args.Contains("--self-test", StringComparer.Ordinal);
        string[] hostArgs = args.Where(argument => argument != "--self-test").ToArray();

        // Preparation is real and complete before the one root provider is built.
        ITypeManager prepared = await TypeManager.CreateAsync(new TypeManagerOptions(typeof(Program).Assembly)).ConfigureAwait(false);
        WebApplicationBuilder builder = WebApplication.CreateBuilder(hostArgs);
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });
        if (selfTest)
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
        }

        HealthcheckOptions healthOptions = HealthcheckOptions.Resolve();
        var source = new CountrySource(selfTest);
        builder.Services.AddSingleton(prepared);
        builder.Services.AddSingleton(source);
        builder.Services.AddSingleton(_ => new HealthcheckService(healthOptions, new HealthApplicationIdentity
        {
            Name = "Startup CountryList",
            InstanceId = Guid.NewGuid()
        }));
        builder.Services.AddSingleton<IAvailabilityMonitor>(provider => provider.GetRequiredService<HealthcheckService>()
            .CreateAvailability(new AvailabilityMonitorOptions { Name = "Usable country list", IsCritical = true }));
        builder.Services.AddSingleton(provider => provider.GetRequiredService<HeartbeatRuntime>().CreateOwner());
        builder.Services.AddSingleton<CountryCatalog>();
        builder.Services.AddSingleton<CountryReader>();
        // Explicit factory aliases enroll these same concrete singleton objects,
        // in this order, without changing Microsoft DI's ownership rules.
        builder.Services.AddSingleton<IStartable>(provider => provider.GetRequiredService<CountryCatalog>());
        builder.Services.AddSingleton<IStartable>(provider => provider.GetRequiredService<CountryReader>());
        builder.Services.AddCisHeartbeat();
        builder.Services.AddCisStartup();

        WebApplication app = builder.Build();
        HealthcheckService health = app.Services.GetRequiredService<HealthcheckService>();
        int businessRequests = 0;
        app.UseCisHealthcheck(health, healthOptions, _ => ValueTask.FromResult(false));
        app.Use(async (context, next) =>
        {
            if (!health.CaptureSnapshot().Ready)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }
            Interlocked.Increment(ref businessRequests);
            await next(context).ConfigureAwait(false);
        });
        app.MapGet("/countries", (CountryReader reader) => reader.ReadCountries());

        Task? startup = null;
        SelfTestReport? report = null;
        try
        {
            startup = app.StartAsync();
            if (selfTest)
            {
                report = await ObserveAsync(app, startup, source, health, prepared, () => Volatile.Read(ref businessRequests)).ConfigureAwait(false);
            }
            else
            {
                await startup.ConfigureAwait(false);
                await app.WaitForShutdownAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            // Observation timeouts never authorize dependency teardown. Release
            // the sample's holds, then join the actual host/module barriers.
            source.ReleaseAll();
            await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
            if (startup is not null)
            {
                try { await startup.ConfigureAwait(false); }
                catch (Exception) when (startup.IsFaulted || startup.IsCanceled)
                {
                    // The startup caller retains its original outcome; this
                    // join only prevents disposing a still-running host start.
                }
            }
            await app.DisposeAsync().ConfigureAwait(false);
        }

        if (report is not null)
        {
            Console.WriteLine(JsonSerializer.Serialize(report with { Stopped = true }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
        return 0;
    }

    private static async Task<SelfTestReport> ObserveAsync(WebApplication app, Task startup, CountrySource source,
        HealthcheckService health, ITypeManager prepared, Func<int> businessRequests)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await Task.WhenAny(source.PrimeEntered, startup).WaitAsync(budget.Token).ConfigureAwait(false);
        if (!source.PrimeEntered.IsCompleted) { await startup.ConfigureAwait(false); }
        Require(source.PrimeEntered.IsCompleted && !startup.IsCompleted, "Priming must hold the actual host startup.");
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(5) };

        int beforeReady = await StatusAsync(client, "/_health/ready", budget.Token).ConfigureAwait(false);
        int beforeBusiness = await StatusAsync(client, "/countries", budget.Token).ConfigureAwait(false);
        var before = new BeforeObservation(beforeReady, beforeBusiness, businessRequests(), !startup.IsCompleted);
        Require(before == new BeforeObservation(500, 503, 0, true), "Health must remain reachable while business side effects are gated.");
        Require(!health.CaptureSnapshot().Application.StartupCompleted && !source.FirstRefreshEntered.IsCompleted,
            "Startup readiness and Heartbeat must remain closed during required priming.");

        source.ReleasePrime();
        await startup.WaitAsync(budget.Token).ConfigureAwait(false);
        CountryCatalog catalog = app.Services.GetRequiredService<CountryCatalog>();
        CountryReader reader = app.Services.GetRequiredService<CountryReader>();
        int afterReady = await StatusAsync(client, "/_health/ready", budget.Token).ConfigureAwait(false);
        using HttpResponseMessage response = await client.GetAsync("/countries", budget.Token).ConfigureAwait(false);
        Country[] countries = await response.Content.ReadFromJsonAsync<Country[]>(budget.Token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The countries response was empty.");
        var after = new AfterObservation(afterReady, (int)response.StatusCode, countries.Length, reader.Started,
            reader.UsesPreparedTypeManager && ReferenceEquals(prepared, app.Services.GetRequiredService<ITypeManager>()),
            catalog.FirstRefreshCompleted.IsCompleted);
        Require(after == new AfterObservation(200, 200, 3, true, true, false), "Readiness must use completed priming without awaiting a refresh.");

        await source.FirstRefreshEntered.WaitAsync(budget.Token).ConfigureAwait(false);
        source.ReleaseRefresh();
        await catalog.FirstRefreshCompleted.WaitAsync(budget.Token).ConfigureAwait(false);
        return new SelfTestReport("Startup.CountryList", before, after, new RefreshObservation(true, catalog.RefreshCount), false);
    }

    private static async Task<int> StatusAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(path, cancellationToken).ConfigureAwait(false);
        return (int)response.StatusCode;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }

    private sealed record BeforeObservation(int ReadinessStatus, int BusinessStatus, int BusinessRequests, bool StartupPending);
    private sealed record AfterObservation(int ReadinessStatus, int BusinessStatus, int CountryCount, bool ReaderStarted, bool SameTypeManager, bool RefreshCompleted);
    private sealed record RefreshObservation(bool Completed, int Count);
    private sealed record SelfTestReport(string Example, BeforeObservation Before, AfterObservation After, RefreshObservation Refresh, bool Stopped);
}
