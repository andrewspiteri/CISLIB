using CIS.Heartbeat.Hosting;
using CIS.RestClient.Hosting;
using CIS.Startup.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CIS.Workflows.Hosting;

internal sealed class PipelineHostedService(PipelineLifetime owner, PipelineHostingOrder order,
    IHostApplicationLifetime lifetime, IOptions<HostOptions> options) : IHostedLifecycleService, IAsyncDisposable
{
    private CancellationTokenRegistration stopping;
    private CancellationTokenRegistration startupCancellation;
    private CancellationTokenRegistration started;

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.ServicesStartConcurrently || options.Value.ServicesStopConcurrently || !order.IsValid())
        {
            throw new InvalidOperationException("Install AddCisWorkflows after ordinary business hosted services and before CIS Heartbeat/Startup bridges; only the exact native web host may follow the cooperating bridges. Disable concurrent hosted starting/stopping.");
        }

        stopping = lifetime.ApplicationStopping.UnsafeRegister(static state => ((PipelineLifetime)state!).BeginStopping(), owner);
        startupCancellation = cancellationToken.UnsafeRegister(static state => ((PipelineLifetime)state!).BeginStopping(), owner);
        started = lifetime.ApplicationStarted.UnsafeRegister(static state => ((PipelineHostedService)state!).ReleaseStartupToken(), this);
        return Task.CompletedTask;
    }

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void ReleaseStartupToken() => startupCancellation.Dispose();

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        owner.BeginStopping();
        return Task.CompletedTask;
    }

    // Reverse Stop order places the listener before this barrier and ordinary
    // business hosted-service teardown after it. Host wait cancellation never
    // substitutes for actual workflow settlement.
    public Task StopAsync(CancellationToken cancellationToken) => owner.StopAsync();
    public Task StoppedAsync(CancellationToken cancellationToken) => owner.StopAsync();

    public async ValueTask DisposeAsync()
    {
        await owner.StopAsync().ConfigureAwait(false);
        startupCancellation.Dispose();
        started.Dispose();
        stopping.Dispose();
    }
}

internal sealed class PipelineHostingOrder(IServiceCollection services)
{
    private static readonly Type? WebHost = typeof(GenericHostWebHostBuilderExtensions).Assembly
        .GetType("Microsoft.AspNetCore.Hosting.GenericWebHostService");

    internal bool IsValid()
    {
        ServiceDescriptor[] hosted = services.Where(item => !item.IsKeyedService
            && item.ServiceType == typeof(IHostedService)).ToArray();
        int position = Array.FindIndex(hosted, item => item.ImplementationType == typeof(PipelineHostedService));
        if (position < 0 || hosted.Count(item => item.ImplementationType == typeof(PipelineHostedService)) != 1)
        {
            return false;
        }

        for (int index = position + 1; index < hosted.Length; index++)
        {
            Type? type = hosted[index].ImplementationType;
            if (type == WebHost && WebHost is not null && index == hosted.Length - 1)
            {
                continue;
            }

            if (type != typeof(RestHostedService) && type != typeof(HeartbeatHostedService)
                && type != typeof(StartupHostedService))
            {
                return false;
            }
        }

        return true;
    }
}
