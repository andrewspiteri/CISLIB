using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using global::OpenTelemetry.Metrics;

namespace CIS.Metrics.OpenTelemetry;

// SDK owns the provider. This narrow bridge proves activation and joins Core callback access before dependencies stop.
internal sealed class MetricsProviderActivation(MetricsCollectionContext context, IServiceProvider root,
    IHostApplicationLifetime lifetime, MetricsHostingOrder order, IOptions<HostOptions> hostOptions) : IHostedLifecycleService, IAsyncDisposable
{
    private CancellationTokenRegistration stopping, startupStopping, started;

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (hostOptions.Value.ServicesStopConcurrently)
        { throw new InvalidOperationException("Disable concurrent hosted stopping so Metrics callback drain precedes dependency teardown."); }
        order.Validate();
        stopping = lifetime.ApplicationStopping.UnsafeRegister(static value => ((MetricsCollectionContext)value!).BeginStopping(), context);
        startupStopping = cancellationToken.UnsafeRegister(static value => ((MetricsCollectionContext)value!).BeginStopping(), context);
        context.Activate(root.GetRequiredService<MeterProvider>());
        started = lifetime.ApplicationStarted.UnsafeRegister(static value => ((MetricsCollectionContext)value!).Runtime.SealStartup(), context);
        return Task.CompletedTask;
    }
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => context.StopAsync();
    public Task StopAsync(CancellationToken cancellationToken) => context.StopAsync();
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public async ValueTask DisposeAsync()
    {
        await context.StopAsync().ConfigureAwait(false);
        startupStopping.Dispose();
        started.Dispose();
        stopping.Dispose();
    }
}
