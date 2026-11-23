using System.Runtime.ExceptionServices;
using CIS.Coordination.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using global::OpenTelemetry.Metrics;

namespace CIS.Metrics.OpenTelemetry;

// SDK owns the provider. This narrow bridge proves activation and joins Core callback access before dependencies stop.
internal sealed class MetricsProviderActivation(MetricsCollectionContext context, IServiceProvider root,
    IHostApplicationLifetime lifetime, MetricsHostingOrder order, IOptions<HostOptions> hostOptions) : IHostedLifecycleService, IAsyncDisposable
{
    private CancellationTokenRegistration stopping;
    private CancellationTokenRegistration startupStopping;
    private CancellationTokenRegistration started;
    private CoordinationHostLifetime? coordination;

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (hostOptions.Value.ServicesStopConcurrently)
        {
            throw new InvalidOperationException("Disable concurrent hosted stopping so Metrics callback drain precedes dependency teardown.");
        }
        order.Validate();
        coordination = root.GetService<CoordinationHostLifetime>();
        stopping = CoordinationHostLifetime.DependencyToken(root, lifetime.ApplicationStopping)
            .UnsafeRegister(static value => ((MetricsCollectionContext)value!).BeginStopping(), context);
        startupStopping = cancellationToken.UnsafeRegister(static value => ((MetricsProviderActivation)value!).RequestStopping(), this);
        context.Activate(root.GetRequiredService<MeterProvider>());
        started = lifetime.ApplicationStarted.UnsafeRegister(static value => ((MetricsProviderActivation)value!).CompleteStartup(), this);
        return Task.CompletedTask;
    }
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => StopOwnersAsync();
    public Task StopAsync(CancellationToken cancellationToken) => StopOwnersAsync();
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopOwnersAsync().ConfigureAwait(false);
        }
        finally
        {
            startupStopping.Dispose();
            started.Dispose();
            stopping.Dispose();
        }
    }

    private void RequestStopping()
    {
        if (coordination is not null)
        {
            coordination.BeginStopping();
        }
        else
        {
            context.BeginStopping();
        }
    }

    private void CompleteStartup()
    {
        context.Runtime.SealStartup();
        if (coordination is not null)
        {
            startupStopping.Dispose();
        }
    }

    private async Task StopOwnersAsync()
    {
        Exception? failure = await CoordinationHostLifetime.CaptureStopAsync(coordination).ConfigureAwait(false);
        try
        {
            await context.StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (failure is not null)
        {
            throw new AggregateException(failure, exception);
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
