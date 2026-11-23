using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using CIS.Healthcheck.AspNetCore;
using CIS.Logging;
using CIS.Security.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CIS.Hosting;

/// <summary>Composes one native host, existing CIS modules and a retained application lifetime.</summary>
/// <remarks>Construction only collects setup. Await disposal even after failed preparation or startup.
/// Stopping is terminal. Complete all registration in the callback; the final provider is built once.</remarks>
public sealed class CISApplication : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly CISApplicationBuilder setup;
    private readonly CISApplicationComposition composition;
    private readonly HostApplicationBuilder? native;
    private readonly WebApplicationBuilder? web;
    private readonly Action<WebApplication>? map;
    private readonly CancellationTokenSource stopping = new();

    private Task? start;
    private Task? stop;
    private Task? disposal;
    private IHost? host;
    private IServiceProvider? buildProvider;
    private Exception? failure;
    private Exception? cutoffFailure;
    private int stopRequested;

    /// <summary>Collects a non-web application with the normal process arguments omitted.</summary>
    public CISApplication() : this([], null)
    {
    }

    /// <summary>Collects a non-web application with application registrations.</summary>
    public CISApplication(Action<CISApplicationBuilder> configure) : this([], configure)
    {
    }

    /// <summary>Collects a non-web application, optionally customizing normal host/services setup.</summary>
    public CISApplication(string[] args, Action<CISApplicationBuilder>? configure = null)
        : this(args, configure, null, webMode: false)
    {
    }

    private CISApplication(string[] args, Action<CISApplicationBuilder>? configure,
        Action<WebApplication>? map, bool webMode)
    {
        ArgumentNullException.ThrowIfNull(args);
        this.map = map;

        if (webMode)
        {
            web = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args.ToArray()
            });
            setup = new(web, web.WebHost);
        }
        else
        {
            native = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args.ToArray());
            setup = new(native, null);
        }

        composition = new(setup);
        try
        {
            configure?.Invoke(setup);
            composition.Seal();

            var validation = new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            };
            var providerFactory = new CISApplicationServiceProviderFactory(validation, CaptureBuildProvider);
            if (web is not null)
            {
                // WebApplicationBuilder applies this factory during its own Build.
                web.Host.UseServiceProviderFactory(providerFactory);
            }
            else
            {
                native!.ConfigureContainer(providerFactory);
            }
        }
        catch
        {
            (setup.Host.Configuration as IDisposable)?.Dispose();
            stopping.Dispose();
            throw;
        }
    }

    /// <summary>Collects one explicit web host; maps native application routes after Build and before startup.</summary>
    public static CISApplication CreateWeb(string[] args, Action<CISApplicationBuilder>? configure,
        Action<WebApplication> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return new(args, configure, map, webMode: true);
    }

    /// <summary>Gets the one built provider after preparation/Build; accessing it never starts the application.</summary>
    public IServiceProvider Services => Volatile.Read(ref host)?.Services
        ?? throw new InvalidOperationException("The application has not been built. Await StartAsync first.");

    /// <summary>Gets the first failure actually observed on the facade's start/run/stop path, if any.</summary>
    public Exception? Failure => Volatile.Read(ref failure);

    /// <summary>Joins the single startup operation; the first caller supplies its operation cancellation.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource completion;
        lock (gate)
        {
            if (start is not null)
            {
                return start;
            }

            ObjectDisposedException.ThrowIf(disposal is not null, this);
            if (stop is not null || Volatile.Read(ref stopRequested) != 0)
            {
                return start = Task.FromCanceled(new CancellationToken(canceled: true));
            }

            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            start = completion.Task;
        }

        // Publish the owned operation before any synchronous user prologue.
        _ = StartCoreAsync(completion, cancellationToken);
        return completion.Task;
    }

    private async Task StartCoreAsync(TaskCompletionSource completion, CancellationToken caller)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(caller, stopping.Token);
        try
        {
            ICISApplicationIntegration[] integrations = await composition.PrepareAsync(operation.Token).ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested();

            IHost built = web is not null ? web.Build() : native!.Build();
            Volatile.Write(ref host, built);
            // The native host now owns this same provider. Only a failed Build
            // leaves the provider for the facade's direct cleanup path.
            Volatile.Write(ref buildProvider, null);

            composition.BindBuiltRoot(built.Services);
            operation.Token.ThrowIfCancellationRequested();

            if (built is WebApplication application)
            {
                application.UseRouting();
                bool securityEnabled = application.Services.GetService<SecurityComposition>() is not null;
                if (securityEnabled)
                {
                    application.UseCisSecurityAuthentication();
                }

                foreach (ICISApplicationIntegration integration in integrations)
                {
                    integration.ConfigureWeb(application);
                }

                IHealthSnapshotSource? healthSource = setup.HealthcheckEnabled
                    ? application.Services.GetService<IHealthSnapshotSource>()
                    : null;
                if (healthSource is not null)
                {
                    HealthcheckService? health = application.Services.GetService<HealthcheckService>();
                    HealthcheckOptions options = health?.Settings ?? HealthcheckOptions.Resolve(setup.Healthcheck);
                    application.UseCisHealthcheck(healthSource, options,
                        setup.AuthorizeHealthDetails ?? (_ => ValueTask.FromResult(false)));
                }

                IHostApplicationLifetime lifetime = application.Services.GetRequiredService<IHostApplicationLifetime>();
                application.Use(async (context, next) =>
                {
                    if (!lifetime.ApplicationStarted.IsCancellationRequested
                        || lifetime.ApplicationStopping.IsCancellationRequested
                        || healthSource is not null && !healthSource.CaptureSnapshot().RequestsAllowed)
                    {
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        return;
                    }

                    await next(context).ConfigureAwait(false);
                });
                if (securityEnabled)
                {
                    application.UseCisSecurityAuthorization();
                }

                map!(application);
            }

            if (Volatile.Read(ref stopRequested) != 0)
            {
                RequestStop();
            }

            operation.Token.ThrowIfCancellationRequested();
            await built.StartAsync(operation.Token).ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested();
            completion.TrySetResult();
        }
        catch (OperationCanceledException exception) when (operation.IsCancellationRequested)
        {
            // Host may observe late cancellation after the real coordinator already failed.
            // Preserve that originating single fault without changing earlier cancellation ownership.
            Task? initialization = composition.StartupOperation?.Operation;
            if (initialization is { IsFaulted: true } && initialization.Exception is { InnerExceptions.Count: 1 } original)
            {
                Exception startupFailure = original.InnerExceptions[0];
                RecordFailure(startupFailure, "startup");
                completion.TrySetException(startupFailure);
            }
            else
            {
                completion.TrySetCanceled(exception.CancellationToken);
            }
        }
        catch (Exception exception)
        {
            RecordFailure(exception, "startup");
            completion.TrySetException(exception);
        }
    }

    private void CaptureBuildProvider(IServiceProvider provider) => Volatile.Write(ref buildProvider, provider);

    /// <summary>Starts, waits for ordinary host shutdown and stops; returns zero for normal/requested stopping.</summary>
    /// <remarks>Disposal is a separate awaited boundary and its failures propagate independently.</remarks>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        using CancellationTokenRegistration requested = cancellationToken.UnsafeRegister(
            static value => ((CISApplication)value!).RequestStop(), this);
        int result = 0;
        try
        {
            await StartAsync(cancellationToken).ConfigureAwait(false);
            IHostApplicationLifetime lifetime = Services.GetRequiredService<IHostApplicationLifetime>();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, lifetime.ApplicationStopping).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
            {
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || stopping.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            RecordFailure(exception, "run");
            result = 1;
        }

        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RecordFailure(exception, "stop");
            result = 1;
        }

        return Failure is null ? result : 1;
    }

    /// <summary>Requests terminal stopping and joins actual owned work. Cancellation only ends this caller's wait.</summary>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource? completion = null;
        Task operation;
        lock (gate)
        {
            if (stop is null)
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                stop = completion.Task;
            }

            operation = stop;
        }

        if (completion is not null)
        {
            _ = StopCoreAsync(completion);
        }

        return cancellationToken.CanBeCanceled ? operation.WaitAsync(cancellationToken) : operation;
    }

    private void RequestStop()
    {
        if (Interlocked.Exchange(ref stopRequested, 1) == 0)
        {
            try
            {
                stopping.Cancel();
            }
            catch (Exception exception)
            {
                Interlocked.CompareExchange(ref cutoffFailure, exception, null);
            }
        }

        try
        {
            Volatile.Read(ref host)?.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
        }
        catch (Exception exception)
        {
            Interlocked.CompareExchange(ref cutoffFailure, exception, null);
        }
    }

    private async Task StopCoreAsync(TaskCompletionSource completion)
    {
        var errors = new List<Exception>();
        try
        {
            RequestStop();
            CloseModuleAdmission(errors);
            Task? startup;
            lock (gate)
            {
                startup = start;
            }

            if (startup is not null)
            {
                try
                {
                    await startup.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Original startup caller retains its outcome.
                }
            }

            if (composition.Coordination is { } coordination)
            {
                await CaptureAsync(coordination.StopAsync, errors).ConfigureAwait(false);
            }

            CloseModuleAdmission(errors);
            if (composition.Audit is { } audit)
            {
                await CaptureAsync(audit.StopAsync, errors).ConfigureAwait(false);
            }
            CloseModuleAdmission(errors);
            if (composition.Messaging is { } messaging)
            {
                await CaptureAsync(messaging.StopAsync, errors).ConfigureAwait(false);
            }
            if (composition.Rest is { } rest)
            {
                await CaptureAsync(rest.StopAsync, errors).ConfigureAwait(false);
            }

            if (composition.Management is { } management)
            {
                await CaptureAsync(management.StopAsync, errors).ConfigureAwait(false);
            }

            if (composition.Metrics is { } metrics)
            {
                await CaptureAsync(metrics.StopAsync, errors).ConfigureAwait(false);
            }

            if (composition.BoundHeartbeat is { } boundHeartbeat)
            {
                await CaptureAsync(() => boundHeartbeat.StopAsync(), errors).ConfigureAwait(false);
            }

            if (Volatile.Read(ref host) is { } built)
            {
                await CaptureAsync(() => built.StopAsync(CancellationToken.None), errors).ConfigureAwait(false);
            }

            // The native bridge drains after the listener and before business
            // hosted teardown. A failed Build/start may have no bridge, so join
            // the actual owner captured during its ordinary DI construction too.
            if (composition.Workflows is { } workflows)
            {
                await CaptureAsync(workflows.StopAsync, errors).ConfigureAwait(false);
            }

            if (composition.Configuration is { } configuration)
            {
                await CaptureAsync(configuration.StopAsync, errors).ConfigureAwait(false);
            }

            if (composition.Heartbeat is { } heartbeat)
            {
                await CaptureAsync(() => heartbeat.StopAsync(), errors).ConfigureAwait(false);
            }

            await StopOwnedLoggingAsync(errors).ConfigureAwait(false);
            if (cutoffFailure is { } cutoff)
            {
                errors.Add(cutoff);
            }

            ThrowErrors(errors);
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            RecordFailure(exception, "stop");
            completion.TrySetException(exception);
        }
    }

    /// <summary>Joins stopping and the one disposal operation, including resources acquired before failed Build.</summary>
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? completion = null;
        Task operation;
        lock (gate)
        {
            if (disposal is null)
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                disposal = completion.Task;
            }

            operation = disposal;
        }

        if (completion is not null)
        {
            _ = DisposeCoreAsync(completion);
        }

        return new(operation);
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        var errors = new List<Exception>();
        try
        {
            await CaptureAsync(() => StopAsync(), errors).ConfigureAwait(false);
            if (Volatile.Read(ref host) is { } built)
            {
                await CaptureAsync(() => DisposeOwnerAsync(built), errors).ConfigureAwait(false);
            }
            else if (Volatile.Read(ref buildProvider) is { } provider)
            {
                // Host resolution failed after provider creation. Preserve its
                // ordinary DI ownership, including async-only services and
                // borrowed instance descriptors, without resolving more services.
                await CaptureAsync(() => DisposeOwnerAsync(provider), errors).ConfigureAwait(false);
                Volatile.Write(ref buildProvider, null);
            }
            else
            {
                if (composition.Heartbeat is { } heartbeat)
                {
                    await CaptureAsync(() => heartbeat.DisposeAsync().AsTask(), errors).ConfigureAwait(false);
                }

                if (composition.Health is { } health)
                {
                    await CaptureAsync(() => health.DisposeAsync().AsTask(), errors).ConfigureAwait(false);
                }

                await CaptureAsync(() =>
                {
                    (setup.Host.Configuration as IDisposable)?.Dispose();
                    return Task.CompletedTask;
                }, errors).ConfigureAwait(false);
            }

            if (composition.Coordination is { } coordination)
            {
                await CaptureAsync(() => coordination.DisposeAsync().AsTask(), errors).ConfigureAwait(false);
            }

            // Preparation precedes DI ownership; Build may fail before its factory is resolved.
            if (composition.Configuration is { } configuration)
            {
                await CaptureAsync(() => configuration.DisposeAsync().AsTask(), errors).ConfigureAwait(false);
            }

            stopping.Dispose();
            ThrowErrors(errors);
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private static async Task DisposeOwnerAsync(object owner)
    {
        if (owner is IAsyncDisposable asynchronous)
        {
            await asynchronous.DisposeAsync().ConfigureAwait(false);
        }
        else if (owner is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private async Task StopOwnedLoggingAsync(List<Exception> errors)
    {
        // Host stopping and module callbacks have already settled. Preserve shutdown
        // logs, then join actual writers before their DI-owned dependencies disappear.
        CoreLogRuntime? effective = composition.OwnsBoundLog ? composition.BoundLog : null;
        if (effective is not null)
        {
            await StopLogAsync(effective).ConfigureAwait(false);
        }

        if (composition.Log is { } original && !ReferenceEquals(original, effective))
        {
            await StopLogAsync(original).ConfigureAwait(false);
        }

        async Task StopLogAsync(CoreLogRuntime log)
        {
            await CaptureAsync(() => log.DisposeAsync().AsTask(), errors).ConfigureAwait(false);
            await CaptureAsync(() => log.Completion, errors).ConfigureAwait(false);
        }
    }

    private void RecordFailure(Exception exception, string phase)
    {
        if (Interlocked.CompareExchange(ref failure, exception, null) is not null)
        {
            return;
        }

        try
        {
            ILogger<CISApplication>? logger = Volatile.Read(ref host)?.Services.GetService<ILogger<CISApplication>>();
            logger?.LogError(exception, "CIS application failed during {Phase}.", phase);
        }
        catch (Exception)
        {
            // Diagnostics do not replace the original application outcome.
        }
    }

    private void CloseModuleAdmission(List<Exception> errors)
    {
        try
        {
            // Signal the reporter before dependency barriers, without joining a callback that may still need REST cancellation.
            composition.Management?.BeginStopping();
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }
        if (composition.Coordination is { IsDrained: false } coordination)
        {
            try
            {
                coordination.BeginStopping();
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            // Admitted jobs can still need credentials, REST, configuration and
            // collectors. The lifetime flags stopping health without closing them.
            return;
        }

        if (composition.Audit is { IsDrained: false } audit)
        {
            Close(audit.BeginStopping);
            return;
        }

        Close(() => composition.Messaging?.BeginStopping());
        Close(() => composition.BoundHealth?.BeginStopping());
        Close(() => composition.Rest?.BeginStopping());
        Close(() => composition.Workflows?.BeginStopping());
        Close(() => composition.Configuration?.BeginStopping());
        Close(() => composition.BoundHeartbeat?.BeginStopping());
        if (composition.Messaging is null)
        {
            Close(() => composition.Metrics?.BeginStopping());
        }

        void Close(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
        }
    }

    private static async Task CaptureAsync(Func<Task> action, List<Exception> errors)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }
    }

    private static void ThrowErrors(List<Exception> errors)
    {
        if (errors.Count == 1)
        {
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        }

        if (errors.Count > 1)
        {
            throw new AggregateException(errors);
        }
    }
}
