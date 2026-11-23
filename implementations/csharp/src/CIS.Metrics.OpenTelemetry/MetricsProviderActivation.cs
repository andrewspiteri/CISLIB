using System.Runtime.ExceptionServices;
using CIS.Coordination.Hosting;
using CIS.Audit.Hosting;
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
    private AuditRuntime? audit;
    private AuditHostingRegistration? auditRegistration;
    private readonly object captureStopGate = new();
    private Task? captureStop;

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (hostOptions.Value.ServicesStopConcurrently)
        {
            throw new InvalidOperationException("Disable concurrent hosted stopping so Metrics callback drain precedes dependency teardown.");
        }
        order.Validate();
        coordination = root.GetService<CoordinationHostLifetime>();
        audit = root.GetService<AuditRuntime>();
        auditRegistration = root.GetService<AuditHostingRegistration>();
        CancellationToken dependency = audit?.DependencyStopping ?? CoordinationHostLifetime.DependencyToken(root, lifetime.ApplicationStopping);
        stopping = audit is null && auditRegistration is not null
            ? dependency.UnsafeRegister(static value => _ = ((MetricsProviderActivation)value!).StopCaptureOnlyAsync(), this)
            : dependency.UnsafeRegister(static value => ((MetricsCollectionContext)value!).BeginStopping(), context);
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
        if (audit is not null)
        {
            audit.BeginStopping();
        }
        else if (coordination is not null)
        {
            coordination.BeginStopping();
        }
        else if (auditRegistration is not null)
        {
            _ = StopCaptureOnlyAsync();
        }
        else
        {
            context.BeginStopping();
        }
    }

    private void CompleteStartup()
    {
        context.Runtime.SealStartup();
        if (coordination is not null || audit is not null || auditRegistration is not null)
        {
            startupStopping.Dispose();
        }
    }

    private async Task StopOwnersAsync()
    {
        Exception? failure = await AuditRuntime.CaptureStopAsync(audit, coordination).ConfigureAwait(false);
        if (audit is null && auditRegistration is not null)
        {
            try
            {
                await StopCaptureOnlyAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = failure is null ? exception : new AggregateException(failure, exception);
            }
        }
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

    private Task StopCaptureOnlyAsync()
    {
        lock (captureStopGate)
        {
            return captureStop ??= CompleteCaptureStopAsync();
        }
    }

    private async Task CompleteCaptureStopAsync()
    {
        Exception? failure = null;
        try
        {
            await auditRegistration!.StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        try
        {
            context.BeginStopping();
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
