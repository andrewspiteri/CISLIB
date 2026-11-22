using CIS.Heartbeat.Hosting;
using CIS.Metrics;
using CIS.Messaging;
using CIS.Startup.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CIS.RestClient.Hosting;

// The existing host owns sequencing. No work is launched by this bridge.
internal sealed class RestHostedService(IRestClientRuntime runtime, IServiceProvider root,
    IHostApplicationLifetime lifetime, RestHostingOrder order, IOptions<HostOptions> options)
    : IHostedLifecycleService, IAsyncDisposable
{
    private readonly MetricsRuntime? metrics = root.GetService<IMetrics>() as MetricsRuntime;
    private readonly MessagingRuntime? messaging = root.GetService<MessagingRuntime>();
    private CancellationTokenRegistration stopping;
    private CancellationTokenRegistration startupStopping;

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (options.Value.ServicesStartConcurrently || options.Value.ServicesStopConcurrently || !order.IsValid())
        {
            throw new InvalidOperationException("Install REST after its dependencies and before the Heartbeat/Startup tail; disable concurrent host start and stop.");
        }
        stopping = lifetime.ApplicationStopping.UnsafeRegister(static state => ((RestHostedService)state!).BeginStopping(), this);
        startupStopping = cancellationToken.UnsafeRegister(static state => ((RestHostedService)state!).BeginStopping(), this);
        return Task.CompletedTask;
    }
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => StopCoreAsync();
    public Task StopAsync(CancellationToken cancellationToken) => StopCoreAsync();
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void BeginStopping()
    {
        messaging?.BeginStopping();
        runtime.BeginStopping();
        if (messaging is null)
        {
            metrics?.BeginStopping();
        }
    }
    private async Task StopCoreAsync()
    {
        BeginStopping();
        Task messagingDrain = messaging?.StopAsync() ?? Task.CompletedTask;
        try
        {
            await messagingDrain.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The original fault remains joined below; it cannot skip REST or Metrics cleanup.
        }
        await Task.WhenAll(messagingDrain, runtime.StopAsync(), metrics?.StopAsync() ?? Task.CompletedTask).ConfigureAwait(false);
    }
    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally { startupStopping.Dispose(); stopping.Dispose(); }
    }
}

internal sealed class RestHostingOrder(IServiceCollection services)
{
    private static readonly Type? WebHost = typeof(GenericHostWebHostBuilderExtensions).Assembly
        .GetType("Microsoft.AspNetCore.Hosting.GenericWebHostService");

    internal bool IsValid()
    {
        ServiceDescriptor[] hosted = services.Where(item => !item.IsKeyedService
            && item.ServiceType == typeof(IHostedService)).ToArray();
        int rest = Array.FindIndex(hosted, item => item.ImplementationType == typeof(RestHostedService));
        if (rest < 0 || hosted.Count(item => item.ImplementationType == typeof(RestHostedService)) != 1)
        {
            return false;
        }
        int cursor = rest + 1;
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
}
