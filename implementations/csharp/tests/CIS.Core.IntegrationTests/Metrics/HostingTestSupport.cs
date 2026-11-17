using System.Net;
using CIS.Metrics;
using CIS.Metrics.OpenTelemetry;
using CIS.Tests.IntegrationTests.Startup;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Metrics;

internal static class HostingTestSupport
{
    internal static readonly TimeSpan Deadline = StartupHost.Deadline;
    internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static void AddMetrics(IServiceCollection services, bool modules = true)
        => services.AddCisMetrics(options =>
        {
            options.CollectCoreModules = modules;
            options.CollectIncomingHttp = false;
            options.ScrapeCacheDuration = TimeSpan.Zero;
            options.ScrapeTimeout = TimeSpan.FromMilliseconds(200);
        });

    internal static void ConfigureApp(WebApplication app)
    {
        app.UseRouting();
        app.UseCisMetrics();
    }

    internal static HttpClient Client(StartupHost host)
    {
        string address = host.App.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new() { BaseAddress = new(address), Timeout = Deadline };
    }

    internal static async Task<string> ScrapeAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/_metrics").WaitAsync(Deadline);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        string text = await response.Content.ReadAsStringAsync();
        CollectionText.AssertIdentity(text);
        return text;
    }

    internal static async Task ObserveStopAsync(Task stop)
    {
        try { await stop.WaitAsync(Deadline); }
        catch (OperationCanceledException) when (stop.IsCanceled) { }
    }

    internal static async Task DisposeHostAsync(IHost host)
    {
        if (host is IAsyncDisposable asynchronous) { await asynchronous.DisposeAsync().AsTask().WaitAsync(Deadline); }
        else { host.Dispose(); }
    }
}

// An actual host-owned callback owner. Its lifecycle signals prove that a
// completed HTTP wait is not permission to stop/dispose the observed service.
internal sealed class HostingCallbackOwner : IHostedLifecycleService, IAsyncDisposable
{
    private IMetrics metrics = null!;
    private int calls, starts, disposals, usable;
    internal TaskCompletionSource Entered { get; } = HostingTestSupport.Signal();
    internal TaskCompletionSource Release { get; } = HostingTestSupport.Signal();
    internal TaskCompletionSource Stopping { get; } = HostingTestSupport.Signal();
    internal int Calls => Volatile.Read(ref calls);
    internal int Starts => Volatile.Read(ref starts);
    internal int Disposals => Volatile.Read(ref disposals);
    internal bool WasUsable => Volatile.Read(ref usable) != 0;

    internal HostingCallbackOwner Bind(IMetrics facade) { metrics = facade; return this; }
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref starts);
        metrics.RegisterObservableGauge(new("app.host.owner"), Observe);
        return Task.CompletedTask;
    }
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) { Stopping.TrySetResult(); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() { Interlocked.Increment(ref disposals); return ValueTask.CompletedTask; }

    private IReadOnlyList<MetricObservation> Observe()
    {
        Interlocked.Increment(ref calls);
        Entered.TrySetResult();
        if (!Release.Task.Wait(HostingTestSupport.Deadline)) { throw new TimeoutException("Controlled callback release was not supplied."); }
        if (!Stopping.Task.IsCompleted && Disposals == 0) { Volatile.Write(ref usable, 1); }
        return [new(1)];
    }
}
