using System.Net;
using CIS.Heartbeat;
using CIS.Heartbeat.Hosting;
using CIS.Metrics.OpenTelemetry;
using CIS.RestClient;
using CIS.RestClient.Hosting;
using CIS.Startup.Hosting;
using CIS.Tests.IntegrationTests.Metrics;
using CIS.Tests.UnitTests.RestClient;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.RestClient;

// Ordinary real host composition over the shared Kestrel fixture and product branch.
internal sealed class RestClientMetricsHost : IAsyncDisposable
{
    private readonly IHost host;
    private readonly WebApplication? app;
    private Task? stop;
    private RestClientMetricsHost(IHost host, WebApplication? app = null) { this.host = host; this.app = app; }
    internal IServiceProvider Services => host.Services;
    internal MetricsCollectionContext Context => Services.GetRequiredService<MetricsCollectionContext>();
    internal IRestClient Rest(string alias = "catalog") => Services.GetRequiredKeyedService<IRestClient>(alias);
    internal HttpClient Client { get; private set; } = null!;

    internal static RestClientMetricsHost Create(RestClientOptions options, bool metricsFirst,
        Action<IServiceCollection>? configure = null, bool heartbeat = false, bool startup = false, bool restLast = false)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Production" });
        builder.Host.UseDefaultServiceProvider(settings => { settings.ValidateScopes = true; settings.ValidateOnBuild = true; });
        builder.Logging.ClearProviders(); builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        Register(builder.Services, options, metricsFirst, configure, heartbeat, startup, restLast);
        WebApplication app = builder.Build();
        app.UseRouting(); app.UseCisMetrics();
        app.MapGet("/alive", () => "alive");
        return new(app, app);
    }

    internal static RestClientMetricsHost CreateHeartbeatHost(RestClientOptions options, bool metricsFirst,
        Action<IServiceCollection> configure, bool restLast)
    {
        IHost host = Host.CreateDefaultBuilder([])
            .UseDefaultServiceProvider(settings => { settings.ValidateScopes = true; settings.ValidateOnBuild = true; })
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureServices(services => Register(services, options, metricsFirst, configure, heartbeat: true, startup: false, restLast: restLast))
            .Build();
        return new(host);
    }

    private static void Register(IServiceCollection services, RestClientOptions options, bool metricsFirst,
        Action<IServiceCollection>? configure, bool heartbeat, bool startup, bool restLast)
    {
        configure?.Invoke(services);
        if (metricsFirst) { Metrics(); }
        if (!restLast) { services.AddCisRestClient(options); }
        if (heartbeat) { services.AddCisHeartbeat(new HeartbeatOptions { MaxConcurrent = 1 }); }
        if (startup) { services.AddCisStartup(); }
        if (restLast) { services.AddCisRestClient(options); }
        if (!metricsFirst) { Metrics(); }

        void Metrics() => services.AddCisMetrics(settings =>
        {
            settings.CollectIncomingHttp = false;
            settings.ScrapeCacheDuration = TimeSpan.Zero;
        });
    }

    internal async Task StartAsync()
    {
        await host.StartAsync().WaitAsync(RestHttpTestSupport.Deadline);
        if (app is not null) { Client = new HttpClient { BaseAddress = new(app.Urls.Single()), Timeout = RestHttpTestSupport.Deadline }; }
    }

    internal async Task<string[]> ScrapeAsync()
    {
        using HttpResponseMessage response = await Client.GetAsync("/_metrics").WaitAsync(RestHttpTestSupport.Deadline);
        string text = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        CollectionText.AssertIdentity(text);
        return CollectionText.Samples(text);
    }

    internal Task StopAsync(CancellationToken token = default) => stop ??= host.StopAsync(token);

    internal static async Task ObserveStopAsync(Task task)
    {
        try { await task.WaitAsync(RestHttpTestSupport.Deadline); }
        catch (OperationCanceledException) when (task.IsCanceled) { }
    }

    public async ValueTask DisposeAsync()
    {
        try { await ObserveStopAsync(StopAsync(CancellationToken.None)); }
        finally
        {
            Client?.Dispose();
            if (host is IAsyncDisposable asynchronous) { await asynchronous.DisposeAsync(); }
            else { host.Dispose(); }
        }
    }
}

internal static class RestClientMetricSamples
{
    internal static double Sum(string[] samples, string name, string? label = null)
        => samples.Where(line => line.StartsWith(name + "{", StringComparison.Ordinal)
            && (label is null || line.Contains(label, StringComparison.Ordinal))).Sum(CollectionText.Value);
}
