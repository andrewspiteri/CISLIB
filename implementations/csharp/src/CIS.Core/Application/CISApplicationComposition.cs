using CIS.Configuration;
using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Heartbeat.Hosting;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using CIS.Metrics;
using CIS.RestClient;
using CIS.RestClient.Hosting;
using CIS.Startup.Hosting;
using CIS.TypeManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CIS.Hosting;

// These are owned instances, not a service locator or another lifecycle registry.
internal sealed class CISApplicationComposition
{
    private readonly CISApplicationBuilder setup;
    private readonly List<ServiceDescriptor> loggingDefaults = [], healthDefaults = [], heartbeatDefaults = [];
    internal CoreLogRuntime? Log { get; private set; }
    internal HealthcheckService? Health { get; private set; }
    internal HeartbeatRuntime? Heartbeat { get; private set; }
    internal ConfigurationRuntime? Configuration { get; set; }
    internal IRestClientRuntime? Rest { get; private set; }
    internal MetricsRuntime? Metrics { get; private set; }
    internal HeartbeatRuntime? BoundHeartbeat { get; private set; }
    internal HealthcheckService? BoundHealth { get; private set; }
    internal HealthcheckOptions HealthOptions { get; private set; } = new();

    internal CISApplicationComposition(CISApplicationBuilder setup)
    {
        this.setup = setup;
        IServiceCollection services = setup.Services;
        // Put lazy defaults before the callback so normal Replace/RemoveAll/ClearProviders work.
        setup.Host.Logging.ClearProviders();
        Add(services, loggingDefaults, ServiceDescriptor.Singleton<CoreLogRuntime>(_ => Log = CoreLogRuntime.Create()));
        Add(services, loggingDefaults, ServiceDescriptor.Singleton<ILoggerProvider>(root =>
            new CoreLoggerProvider(root.GetRequiredService<CoreLogRuntime>())));
        Add(services, healthDefaults, ServiceDescriptor.Singleton<HealthcheckService>(root => Health = new(
            HealthOptions, new HealthApplicationIdentity
            {
                Name = EnvironmentOptions.ApplicationName, InstanceId = Guid.Parse(EnvironmentOptions.InstanceId),
                BuildVersion = EnvironmentOptions.BuildVersion, ServiceVersion = EnvironmentOptions.ServiceVersion,
                HostName = EnvironmentOptions.HostName, OsDescription = EnvironmentOptions.OperatingSystemDescription
            }, root.GetService<TimeProvider>())));
        Add(services, heartbeatDefaults, ServiceDescriptor.Singleton<HeartbeatRuntime>(_ => Heartbeat = new(setup.Heartbeat)));
    }

    internal void Seal()
    {
        ArgumentNullException.ThrowIfNull(setup.TypeManager);
        ArgumentNullException.ThrowIfNull(setup.Healthcheck);
        string name = setup.TypeManager.HostAssembly?.GetName().Name
            ?? throw new InvalidOperationException("Type Manager requires the actual managed host assembly.");
        if (setup.Configuration is { } configured && configured.HostApplicationName != name)
        { throw new ArgumentException("Configuration scope must match the selected host assembly name."); }
        if (setup.Services.Any(item => item.ServiceType == typeof(ITypeManager)))
        { throw new InvalidOperationException("Configure TypeManager options; the facade registers its exact prepared instance."); }
        HealthOptions = setup.HealthcheckEnabled ? HealthcheckOptions.Resolve(setup.Healthcheck) : setup.Healthcheck;
        if (!setup.LoggingEnabled) { Remove(loggingDefaults); }
        if (!setup.HealthcheckEnabled) { Remove(healthDefaults); }
        if (!setup.HeartbeatEnabled) { Remove(heartbeatDefaults); }
        // RemoveAll is intentional too; AddCisHeartbeat must not restore a removed default.
        setup.HeartbeatEnabled = setup.HeartbeatEnabled && setup.Services.Any(item => !item.IsKeyedService
            && item.ServiceType == typeof(HeartbeatRuntime));
        // Capture mutable Heartbeat options before asynchronous preparation.
        if (setup.Heartbeat is { } heartbeat) { setup.Heartbeat = new HeartbeatOptions { MaxConcurrent = heartbeat.MaxConcurrent }; }
    }

    internal async Task<ICISApplicationIntegration[]> PrepareAsync(CancellationToken token)
    {
        ITypeManager manager = await TypeManager.CreateAsync(setup.TypeManager, token).ConfigureAwait(false);
        Configuration = await ConfigurationRuntime.PrepareAsync(manager,
            setup.Configuration ?? new ConfigurationOptions(setup.TypeManager.HostAssembly.GetName().Name!)
            { IsHealthCritical = true }, token).ConfigureAwait(false);
        setup.Services.AddSingleton(manager);
        Configuration.Register(setup.Services, prependStartup: true);
        foreach (RestClientOptions client in setup.RestClients) { setup.Services.AddCisRestClient(client); }
        ICISApplicationIntegration[] integrations = setup.EnableOptionalIntegrations
            ? manager.GetImplementations<ICISApplicationIntegration>().Distinct()
                .OrderBy(type => type.AssemblyQualifiedName, StringComparer.Ordinal)
                .Select(CreateIntegration).ToArray() : [];
        foreach (ICISApplicationIntegration integration in integrations) { integration.ConfigureServices(setup.Services); }
        setup.Services.TryAddSingleton<IMetrics>(DisabledMetrics.Instance);
        if (setup.HeartbeatEnabled) { setup.Services.AddCisHeartbeat(setup.Heartbeat); }
        setup.Services.AddCisStartup();
        if (setup.HealthcheckEnabled)
        { setup.Services.AddSingleton<IHostedService>(root => new ApplicationHealthPipe(root.GetService<HealthcheckService>())); }
        // Move only the exact cooperating bridges, preserving application registration order.
        MoveTail(typeof(RestHostedService));
        if (setup.HeartbeatEnabled) { MoveTail(typeof(HeartbeatHostedService)); }
        MoveTail(typeof(StartupHostedService));
        setup.Services.AddOptions<HostOptions>().Validate(options =>
            !options.ServicesStartConcurrently && !options.ServicesStopConcurrently,
            "CISApplication requires sequential hosted starting and stopping.");
        return integrations;
    }

    internal void BindBuiltRoot(IServiceProvider root)
    {
        // Retain the effective module owners before route customization can use them.
        // This also covers a callback failing before any hosted cutoff has subscribed.
        BoundHealth = root.GetService<HealthcheckService>();
        BoundHeartbeat = root.GetService<HeartbeatRuntime>();
        Metrics = root.GetService<IMetrics>() as MetricsRuntime;
        Rest = root.GetService<IRestClientRuntime>();
    }

    private static ICISApplicationIntegration CreateIntegration(Type type)
    {
        if (!type.IsPublic || type.GetConstructor(Type.EmptyTypes) is null)
        { throw new InvalidOperationException("An optional CIS registrar must be public with a parameterless constructor."); }
        return (ICISApplicationIntegration)Activator.CreateInstance(type)!;
    }

    private void MoveTail(Type type)
    {
        ServiceDescriptor[] descriptors = setup.Services.Where(item => !item.IsKeyedService
            && item.ServiceType == typeof(IHostedService) && item.ImplementationType == type).ToArray();
        if (descriptors.Length > 1) { throw new InvalidOperationException("Conflicting CIS hosted bridges are registered."); }
        foreach (ServiceDescriptor descriptor in descriptors) { setup.Services.Remove(descriptor); setup.Services.Add(descriptor); }
    }
    private static void Add(IServiceCollection services, List<ServiceDescriptor> owned, ServiceDescriptor descriptor)
    { services.Add(descriptor); owned.Add(descriptor); }
    private void Remove(IEnumerable<ServiceDescriptor> descriptors)
    { foreach (ServiceDescriptor descriptor in descriptors) { setup.Services.Remove(descriptor); } }

    private sealed class ApplicationHealthPipe(HealthcheckService? health) : IHostedService, IAsyncDisposable
    {
        private readonly HealthPipeServer? server = health is null ? null : new(health, health.Settings);
        public Task StartAsync(CancellationToken cancellationToken) => server?.StartAsync(cancellationToken) ?? Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => server?.StopAsync() ?? Task.CompletedTask;
        public ValueTask DisposeAsync() => server?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
