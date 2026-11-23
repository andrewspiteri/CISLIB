using CIS.Healthcheck;
using CIS.Heartbeat.Hosting;
using CIS.Jobs;
using CIS.Jobs.Hosting;
using CIS.Startup;
using CIS.Startup.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CIS.Coordination.Hosting;

internal sealed class CoordinationStartable(IServiceProvider root) : IStartable
{
    private readonly object gate = new();
    private Task? starting;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource? completion = null;
        Task operation;
        lock (gate)
        {
            if (starting is null)
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                starting = completion.Task;
            }

            operation = starting;
        }

        if (completion is not null)
        {
            _ = StartCoreAsync(completion, cancellationToken);
        }
        return operation;
    }

    private async Task StartCoreAsync(TaskCompletionSource completion, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            CoordinationHostLifetime lifetime = root.GetRequiredService<CoordinationHostLifetime>();
            HealthcheckService? health = root.GetService<HealthcheckService>();
            if (root.GetService<IHealthMonitorFactory>() is { } monitors)
            {
                lifetime.BindStoppingMonitor(monitors.CreateCondition(new()
                {
                    Name = "Coordination stopping",
                    IsCritical = true,
                    InitiallyFlagged = false
                }));
            }

            if (health is not null)
            {
                lifetime.BindHealth(health);
            }
            CoordinationInstrumentation observations = root.GetRequiredService<CoordinationInstrumentation>();
            await root.GetRequiredService<CoordinationRuntime>().StartAsync(cancellationToken).ConfigureAwait(false);
            observations.ActivateHealth(health, root.GetRequiredService<CoordinationHostingPolicy>().IsHealthCritical);
            cancellationToken.ThrowIfCancellationRequested();
            if (root.GetService<JobsComposition>() is { } jobsComposition)
            {
                JobInstrumentation jobObservations = root.GetRequiredService<JobInstrumentation>();
                root.GetService<IJobConfigurationBinding>()?.Connect();
                cancellationToken.ThrowIfCancellationRequested();
                await root.GetRequiredService<JobRuntime>().StartAsync(cancellationToken).ConfigureAwait(false);
                jobObservations.ActivateHealth(health, jobsComposition.Options.IsHealthCritical);
                await root.GetRequiredService<JobHostingSupervisor>().StartAsync(cancellationToken).ConfigureAwait(false);
            }

            completion.TrySetResult();
        }
        catch (OperationCanceledException error) { completion.TrySetCanceled(error.CancellationToken); }
        catch (Exception error) { completion.TrySetException(error); }
    }
}

internal sealed class CoordinationHostedService(CoordinationHostLifetime lifetime, IServiceProvider root,
    IHostApplicationLifetime host, CoordinationHostingOrder order, IOptions<HostOptions> options) : IHostedLifecycleService, IAsyncDisposable
{
    private CancellationTokenRegistration startupStopping;
    private CancellationTokenRegistration started;
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (options.Value.ServicesStartConcurrently || options.Value.ServicesStopConcurrently || !order.IsValid())
        {
            throw new InvalidOperationException("Install Coordination after ordinary/Messaging/Workflow/REST dependencies and before Heartbeat/Startup; keep sequential host start/stop and the coordination initializer last.");
        }

        startupStopping = cancellationToken.UnsafeRegister(static state => ((CoordinationHostLifetime)state!).BeginStopping(), lifetime);
        started = host.ApplicationStarted.UnsafeRegister(static state => ((CoordinationHostedService)state!).ReleaseStartup(), this);
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => order.HasStartup ? Task.CompletedTask
        : root.GetServices<IStartable>().OfType<CoordinationStartable>().Single().StartAsync(cancellationToken);
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => lifetime.StopAsync();
    public Task StopAsync(CancellationToken cancellationToken) => lifetime.StopAsync();
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public async ValueTask DisposeAsync()
    {
        try
        {
            await lifetime.StopAsync().ConfigureAwait(false);
        }
        finally { started.Dispose(); startupStopping.Dispose(); }
    }

    private void ReleaseStartup() => startupStopping.Dispose();
}

internal sealed class CoordinationHostingOrder(IServiceCollection services)
{
    private static readonly Type? WebHost = typeof(GenericHostWebHostBuilderExtensions).Assembly
        .GetType("Microsoft.AspNetCore.Hosting.GenericWebHostService");
    internal bool HasStartup => Hosted().Any(item => item.ImplementationType == typeof(StartupHostedService));

    internal bool IsValid()
    {
        ServiceDescriptor[] hosted = Hosted();
        int position = Array.FindIndex(hosted, item => item.ImplementationType == typeof(CoordinationHostedService));
        if (position < 0 || hosted.Count(item => item.ImplementationType == typeof(CoordinationHostedService)) != 1)
        {
            return false;
        }
        ServiceDescriptor[] participants = services.Where(item => !item.IsKeyedService && item.ServiceType == typeof(IStartable)).ToArray();
        if (participants.Length == 0 || participants[^1].ImplementationType != typeof(CoordinationStartable))
        {
            return false;
        }
        int cursor = position + 1;
        if (cursor < hosted.Length && hosted[cursor].ImplementationType == typeof(HeartbeatHostedService))
        {
            cursor++;
        }
        if (cursor < hosted.Length && hosted[cursor].ImplementationType == typeof(StartupHostedService))
        {
            cursor++;
        }
        if (cursor < hosted.Length && WebHost is not null && hosted[cursor].ImplementationType == WebHost)
        {
            cursor++;
        }
        return cursor == hosted.Length;
    }

    private ServiceDescriptor[] Hosted() => services.Where(item => !item.IsKeyedService && item.ServiceType == typeof(IHostedService)).ToArray();
}
