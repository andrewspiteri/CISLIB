using CIS.Audit.Hosting;
using CIS.Configuration;
using CIS.Coordination.Hosting;
using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Heartbeat.Hosting;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using CIS.Management.Hosting;
using CIS.Metrics;
using CIS.Messaging;
using CIS.Messaging.Hosting;
using CIS.RestClient;
using CIS.RestClient.Hosting;
using CIS.Startup.Hosting;
using CIS.TypeManagement;
using CIS.Workflows.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CIS.Hosting;

// These are owned instances, not a service locator or another lifecycle registry.
internal sealed class CISApplicationComposition
{
    private readonly CISApplicationBuilder setup;
    private readonly List<ServiceDescriptor> loggingDefaults = [];
    private readonly List<ServiceDescriptor> healthDefaults = [];
    private readonly List<ServiceDescriptor> heartbeatDefaults = [];
    private PipelineHostingRegistration? workflows;

    internal PipelineHostingRegistration? Workflows => workflows;
    internal AuditHostingRegistration? Audit
    {
        get; private set;
    }
    internal CoordinationHostLifetime? Coordination
    {
        get; private set;
    }
    internal MessagingRuntime? Messaging
    {
        get; private set;
    }
    internal ManagementRegistrationLifetime? Management
    {
        get; private set;
    }

    internal CoreLogRuntime? Log
    {
        get; private set;
    }
    internal CoreLogRuntime? BoundLog
    {
        get; private set;
    }
    internal bool OwnsBoundLog
    {
        get; private set;
    }
    internal StartupOperationReference? StartupOperation
    {
        get; private set;
    }
    internal HealthcheckService? Health
    {
        get; private set;
    }
    internal HeartbeatRuntime? Heartbeat
    {
        get; private set;
    }
    internal ConfigurationRuntime? Configuration
    {
        get; set;
    }
    internal IRestClientRuntime? Rest
    {
        get; private set;
    }
    internal MetricsRuntime? Metrics
    {
        get; private set;
    }
    internal HeartbeatRuntime? BoundHeartbeat
    {
        get; private set;
    }
    internal HealthcheckService? BoundHealth
    {
        get; private set;
    }

    internal CISApplicationComposition(CISApplicationBuilder setup)
    {
        this.setup = setup;
        IServiceCollection services = setup.Services;

        // Put lazy defaults before the callback so normal Replace/RemoveAll/ClearProviders work.
        setup.Host.Logging.ClearProviders();
        Add(services, loggingDefaults, ServiceDescriptor.Singleton<CoreLogRuntime>(_ => Log = CoreLogRuntime.Create()));
        Add(services, loggingDefaults, ServiceDescriptor.Singleton<ILoggerProvider>(root =>
            new CoreLoggerProvider(root.GetRequiredService<CoreLogRuntime>())));

        Add(services, healthDefaults, ServiceDescriptor.Singleton<HealthcheckService>(root =>
        {
            ArgumentNullException.ThrowIfNull(setup.Healthcheck);
            return Health = new(HealthcheckOptions.Resolve(setup.Healthcheck), new HealthApplicationIdentity
            {
                Name = EnvironmentOptions.ApplicationName,
                InstanceId = Guid.Parse(EnvironmentOptions.InstanceId),
                BuildVersion = EnvironmentOptions.BuildVersion,
                ServiceVersion = EnvironmentOptions.ServiceVersion,
                HostName = EnvironmentOptions.HostName,
                OsDescription = EnvironmentOptions.OperatingSystemDescription
            }, root.GetService<TimeProvider>(), root.GetServices<IHealthReadinessSource>());
        }));
        Add(services, healthDefaults, ServiceDescriptor.Singleton<IHealthMonitorFactory>(root =>
            root.GetRequiredService<HealthcheckService>()));
        Add(services, healthDefaults, ServiceDescriptor.Singleton<IHealthSnapshotSource>(root =>
            root.GetRequiredService<HealthcheckService>()));

        Add(services, heartbeatDefaults, ServiceDescriptor.Singleton<HeartbeatRuntime>(_ => Heartbeat = new(setup.Heartbeat)));
        Add(services, heartbeatDefaults, ServiceDescriptor.Singleton<IHeartbeatOwnerFactory>(root =>
            root.GetRequiredService<HeartbeatRuntime>()));
    }

    internal void Seal()
    {
        CaptureManagement();
        CaptureAudit();
        CaptureCoordination();
        workflows = setup.Services.LastOrDefault(item => !item.IsKeyedService
            && item.ServiceType == typeof(PipelineHostingRegistration))?.ImplementationInstance as PipelineHostingRegistration;
        ArgumentNullException.ThrowIfNull(setup.TypeManager);
        string name = setup.TypeManager.HostAssembly?.GetName().Name
            ?? throw new InvalidOperationException("Type Manager requires the actual managed host assembly.");
        if (setup.Configuration is { } configured && configured.HostApplicationName != name)
        {
            throw new ArgumentException("Configuration scope must match the selected host assembly name.");
        }
        if (setup.Services.Any(item => item.ServiceType == typeof(ITypeManager)))
        {
            throw new InvalidOperationException("Configure TypeManager options; the facade registers its exact prepared instance.");
        }
        if (!setup.LoggingEnabled)
        {
            Remove(loggingDefaults);
        }
        if (!setup.HealthcheckEnabled)
        {
            Remove(healthDefaults);
        }
        if (!setup.HeartbeatEnabled)
        {
            Remove(heartbeatDefaults);
        }

        // RemoveAll is intentional too; later hosting must not restore the default.
        setup.HeartbeatEnabled = setup.HeartbeatEnabled && setup.Services.Any(item => !item.IsKeyedService
            && item.ServiceType == typeof(HeartbeatRuntime));

        // Capture mutable Heartbeat options before asynchronous preparation.
        if (setup.Heartbeat is { } heartbeat)
        {
            setup.Heartbeat = new HeartbeatOptions { MaxConcurrent = heartbeat.MaxConcurrent };
        }
    }

    internal async Task<ICISApplicationIntegration[]> PrepareAsync(CancellationToken token)
    {
        ITypeManager manager = await TypeManager.CreateAsync(setup.TypeManager, token).ConfigureAwait(false);
        Configuration = await ConfigurationRuntime.PrepareAsync(manager,
            setup.Configuration ?? new ConfigurationOptions(setup.TypeManager.HostAssembly.GetName().Name!)
            {
                IsHealthCritical = true
            }, token).ConfigureAwait(false);
        setup.Services.AddSingleton(manager);
        Configuration.Register(setup.Services, prependStartup: true);

        foreach (RestClientOptions client in setup.RestClients)
        {
            setup.Services.AddCisRestClient(client);
        }
        ICISApplicationIntegration[] integrations = setup.EnableOptionalIntegrations
            ? manager.GetImplementations<ICISApplicationIntegration>().Distinct()
                .OrderBy(type => type.AssemblyQualifiedName, StringComparer.Ordinal)
                .Select(CreateIntegration).ToArray() : [];
        foreach (ICISApplicationIntegration integration in integrations)
        {
            integration.ConfigureServices(setup.Services);
        }
        CaptureManagement();
        CaptureAudit();
        CaptureCoordination();
        setup.Services.TryAddSingleton<IMetrics>(DisabledMetrics.Instance);
        if (setup.MessagingEnabled)
        {
            setup.Services.AddCisMessaging();
        }
        if (setup.AuditEnabled)
        {
            setup.Services.AddCisAudit();
        }
        if (setup.HeartbeatEnabled)
        {
            // Its business alias was already offered before the user's callback.
            setup.Services.AddCisHeartbeatCore(setup.Heartbeat, registerOwnerFactory: false);
        }
        setup.Services.AddCisStartup();
        ServiceDescriptor? coordinationStartup = setup.Services.SingleOrDefault(item => !item.IsKeyedService
            && item.ServiceType == typeof(CIS.Startup.IStartable) && item.ImplementationType == typeof(CoordinationStartable));
        if (coordinationStartup is not null)
        {
            setup.Services.Remove(coordinationStartup);
            setup.Services.Add(coordinationStartup);
        }

        if (setup.HealthcheckEnabled)
        {
            setup.Services.AddSingleton<IHostedService>(CreateHealthPipe);
        }

        // Move only the exact cooperating bridges, preserving application registration order.
        MoveTail(typeof(MessagingHostedService));
        MoveTail(typeof(AuditHostedService));
        MoveTail(typeof(PipelineHostedService));
        MoveTail(typeof(RestHostedService));
        MoveTail(typeof(CoordinationHostedService));
        if (setup.HeartbeatEnabled)
        {
            MoveTail(typeof(HeartbeatHostedService));
        }
        MoveTail(typeof(StartupHostedService));
        setup.Services.AddOptions<HostOptions>().Validate(options =>
            !options.ServicesStartConcurrently && !options.ServicesStopConcurrently,
            "CISApplication requires sequential hosted starting and stopping.");

        NormalizeDefaultAlias(healthDefaults, typeof(IHealthMonitorFactory), typeof(HealthcheckService));
        NormalizeDefaultAlias(healthDefaults, typeof(IHealthSnapshotSource), typeof(HealthcheckService));
        NormalizeDefaultAlias(heartbeatDefaults, typeof(IHeartbeatOwnerFactory), typeof(HeartbeatRuntime));
        CaptureLoggingRegistration();
        return integrations;
    }

    internal void BindBuiltRoot(IServiceProvider root)
    {
        // Retain the effective module owners before route customization can use them.
        // This also covers a callback failing before any hosted cutoff has subscribed.
        StartupOperation = root.GetService<StartupOperationReference>();
        BoundLog = root.GetService<CoreLogRuntime>();
        BoundHealth = root.GetService<HealthcheckService>();
        BoundHeartbeat = root.GetService<HeartbeatRuntime>();
        Metrics = root.GetService<IMetrics>() as MetricsRuntime;
        Rest = root.GetService<IRestClientRuntime>();
        Messaging = root.GetService<MessagingRuntime>();
    }

    private void CaptureAudit()
    {
        Audit = setup.Services.LastOrDefault(item => !item.IsKeyedService
            && item.ServiceType == typeof(AuditHostingRegistration))?.ImplementationInstance as AuditHostingRegistration;
    }

    private void CaptureManagement()
    {
        Management = setup.Services.LastOrDefault(item => !item.IsKeyedService
            && item.ServiceType == typeof(ManagementRegistrationLifetime))?.ImplementationInstance as ManagementRegistrationLifetime;
    }

    private void CaptureCoordination()
    {
        // Registration metadata exists before Build can construct any owners or fail.
        // Resolve no provider here: failed-Build cleanup must join the same lifetime.
        Coordination = (setup.Services.LastOrDefault(item => !item.IsKeyedService
            && item.ServiceType == typeof(CoordinationComposition))?.ImplementationInstance
                as CoordinationComposition)?.Lifetime;
    }

    private IHostedService CreateHealthPipe(IServiceProvider root)
    {
        IHealthSnapshotSource? source = root.GetService<IHealthSnapshotSource>();
        HealthcheckOptions? options = source is null ? null
            : root.GetService<HealthcheckService>()?.Settings ?? HealthcheckOptions.Resolve(setup.Healthcheck);
        return new ApplicationHealthPipe(source, options);
    }

    private void NormalizeDefaultAlias(List<ServiceDescriptor> defaults, Type contract, Type concrete)
    {
        ServiceDescriptor? alias = defaults.SingleOrDefault(item => item.ServiceType == contract);
        if (alias is null || !setup.Services.Contains(alias))
        {
            return;
        }

        ServiceDescriptor? target = setup.Services.LastOrDefault(item => !item.IsKeyedService && item.ServiceType == concrete);
        if (target is null)
        {
            setup.Services.Remove(alias);
            return;
        }
        if (target.ImplementationInstance is not { } borrowed)
        {
            return;
        }

        // Factory aliases would make standard DI capture this borrowed disposable.
        // Normalize only our still-present default, preserving every explicit override.
        ServiceDescriptor replacement = ServiceDescriptor.Singleton(contract, borrowed);
        int index = setup.Services.IndexOf(alias);
        setup.Services[index] = replacement;
        defaults[defaults.IndexOf(alias)] = replacement;
    }

    private void CaptureLoggingRegistration()
    {
        ServiceDescriptor? descriptor = setup.Services.LastOrDefault(item => !item.IsKeyedService
            && item.ServiceType == typeof(CoreLogRuntime));
        if (descriptor is null)
        {
            return;
        }
        OwnsBoundLog = descriptor.ImplementationInstance is null;
        if (descriptor.ImplementationFactory is not { } factory)
        {
            return;
        }

        // Capture only the effective owned factory, including if a later Build step fails.
        // Keep its lifetime and ordinary DI disposal; an instance registration remains borrowed.
        int index = setup.Services.IndexOf(descriptor);
        setup.Services[index] = ServiceDescriptor.Describe(typeof(CoreLogRuntime), root =>
        {
            object runtime = factory(root);
            BoundLog = runtime as CoreLogRuntime;
            return runtime;
        }, descriptor.Lifetime);
    }

    private static ICISApplicationIntegration CreateIntegration(Type type)
    {
        if (!type.IsPublic || type.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException("An optional CIS registrar must be public with a parameterless constructor.");
        }
        return (ICISApplicationIntegration)Activator.CreateInstance(type)!;
    }

    private void MoveTail(Type type)
    {
        ServiceDescriptor[] descriptors = setup.Services.Where(item => !item.IsKeyedService
            && item.ServiceType == typeof(IHostedService) && item.ImplementationType == type).ToArray();
        if (descriptors.Length > 1)
        {
            throw new InvalidOperationException("Conflicting CIS hosted bridges are registered.");
        }
        foreach (ServiceDescriptor descriptor in descriptors)
        {
            setup.Services.Remove(descriptor);
            setup.Services.Add(descriptor);
        }
    }

    private static void Add(IServiceCollection services, List<ServiceDescriptor> owned, ServiceDescriptor descriptor)
    {
        services.Add(descriptor);
        owned.Add(descriptor);
    }

    private void Remove(IEnumerable<ServiceDescriptor> descriptors)
    {
        foreach (ServiceDescriptor descriptor in descriptors)
        {
            setup.Services.Remove(descriptor);
        }
    }

    private sealed class ApplicationHealthPipe(IHealthSnapshotSource? source, HealthcheckOptions? options) : IHostedService, IAsyncDisposable
    {
        private readonly HealthPipeServer? server = source is null ? null : new(source, options!);

        public Task StartAsync(CancellationToken cancellationToken) => server?.StartAsync(cancellationToken) ?? Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => server?.StopAsync() ?? Task.CompletedTask;
        public ValueTask DisposeAsync() => server?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
