using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CIS.Configuration.Hosting;

/// <summary>Connects prepared retained Configuration to the existing sequential Startup and optional Heartbeat/Healthcheck integrations.</summary>
public static class ConfigurationHostingExtensions
{
    /// <summary>Registers an already loaded runtime before dependent unkeyed IStartable services; install AddCisStartup on the same host.</summary>
    /// <remarks>Use one prepared runtime and one root Build per host. Keep caller-owned async cleanup around preparation/Build failures; successful DI resolution also owns the shared idempotent runtime disposal.</remarks>
    public static IServiceCollection AddCisConfiguration(this IServiceCollection services, ConfigurationRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(runtime);
        runtime.Register(services);
        return services;
    }
}

internal sealed class ConfigurationStartable : IStartable
{
    private readonly IServiceProvider root;
    private readonly ConfigurationRuntime runtime;

    public ConfigurationStartable(IServiceProvider root, ConfigurationRuntime runtime)
    {
        runtime.ValidateStartupOrder();
        this.root = root;
        this.runtime = runtime;
    }

    public Task StartAsync(CancellationToken cancellationToken)
        => runtime.ActivateAsync(root.GetService<HeartbeatRuntime>(), root.GetService<HealthcheckService>(),
            root.GetService<ILogger<ConfigurationRuntime>>(), cancellationToken);
}
