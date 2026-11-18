using System.Diagnostics.Metrics;
using CIS.Configuration;
using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using CIS.TypeManagement;
using CIS.RestClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using global::OpenTelemetry;
using global::OpenTelemetry.Metrics;
using global::OpenTelemetry.Exporter;
using global::OpenTelemetry.Resources;

namespace CIS.Metrics.OpenTelemetry;

internal sealed class MetricsCollectionContext : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly IServiceProvider root;
    private readonly MetricsStreamCatalog catalog;
    private readonly MetricsRegistrationGuard guard;
    private readonly RestClientComposition? restComposition;
    private ILogger<MetricsCollectionContext>? logger;
    private int active, stopping;

    internal MetricsCollectionContext(IServiceProvider root, MetricsSettings settings, MetricsRegistrationGuard guard,
        RestClientComposition? restComposition = null)
    {
        this.root = root;
        this.guard = guard;
        this.restComposition = restComposition;
        settings = settings.ForComposition(restComposition is not null);
        Settings = settings;
        IMeterFactory factory = root.GetRequiredService<IMeterFactory>();
        CisMeter = factory.Create("CIS.Metrics", "1.0.0");
        HostingMeter = factory.Create("Microsoft.AspNetCore.Hosting");
        OutgoingMeter = settings.CollectOutgoingHttp ? factory.Create("System.Net.Http") : null;
        catalog = new(settings);
        Runtime = new(CisMeter, new MetricsRuntimeOptions
        {
            MaxDefinitions = settings.MaxDefinitions,
            MaxReservedNames = checked(settings.MaxStreams * 5 + 2),
        }, category => logger?.LogWarning("CIS Metrics dropped an observation or registration ({Category}).", category),
            admitDefinition: catalog.Admit);
        Runtime.ReserveExportedNames(catalog.NativeExportedNames);
        HttpPolicy = new(settings);
    }

    internal MetricsSettings Settings { get; }
    internal MetricsRuntime Runtime { get; }
    internal MetricsHttpPolicy HttpPolicy { get; }
    internal IMetrics Metrics => Settings.Enabled ? Runtime : DisabledMetrics.Instance;
    internal Meter CisMeter { get; }
    internal Meter HostingMeter { get; }
    internal Meter? OutgoingMeter { get; }
    internal bool IsEnabled => Settings.Enabled;
    internal bool IsActive => Volatile.Read(ref active) != 0 && !IsStopping;
    internal bool IsStopping => Volatile.Read(ref stopping) != 0 || Runtime.IsStopping;
    internal int DefinitionCount => Runtime.Definitions.Count;
    internal int ReservedNameCount => Runtime.ReservedExportedNames.Count;
    internal IReadOnlyList<MetricsStreamSnapshot> CaptureCatalog() => catalog.Capture();
    internal MetricStreamConfiguration Select(Instrument instrument) => catalog.Select(instrument, Runtime, HostingMeter, OutgoingMeter);

    internal ResourceBuilder Resource()
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal)
        { ["application"] = Settings.Application, ["environment"] = Settings.Environment };
        foreach ((string key, string value) in Settings.ResourceAttributes) { values.Add(key, value); }
        return ResourceBuilder.CreateEmpty().AddAttributes(values);
    }

    internal void Activate(MeterProvider provider)
    {
        // A replacement provider factory could bypass all SDK deferred callbacks; validate again independently.
        guard.Validate();
        MetricsExporterPolicy.Validate(root.GetRequiredService<IOptionsMonitor<PrometheusAspNetCoreOptions>>().Get(Options.DefaultName), Settings);
        // Read the actual finished provider, not the builder's desired resource.
        var attributes = provider.GetResource().Attributes.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (attributes.Count != Settings.ResourceAttributes.Count + 2
            || !attributes.TryGetValue("application", out object? application) || !Equals(application, Settings.Application)
            || !attributes.TryGetValue("environment", out object? environment) || !Equals(environment, Settings.Environment)
            || Settings.ResourceAttributes.Any(pair => !attributes.TryGetValue(pair.Key, out object? value) || !Equals(value, pair.Value)))
        { throw new InvalidOperationException("The active Metrics provider must retain the exact approved resource snapshot and authoritative application/environment identity."); }
        logger = root.GetService<ILogger<MetricsCollectionContext>>();
        if (Settings.CollectCoreModules) { AttachCoreSources(); }
        lock (gate)
        {
            if (IsStopping) { throw new OperationCanceledException("Metrics activation lost to host stopping."); }
            Volatile.Write(ref active, 1);
        }
    }

    private void AttachCoreSources()
    {
        Attach(root.GetService<CoreLogRuntime>());
        Attach(root.GetService<HeartbeatRuntime>());
        Attach(root.GetService<HealthcheckService>());
        // Existing providers borrow their runtime; enabling Metrics does not change that ownership
        // or require an application to register the same precreated runtime under another key.
        foreach (CoreLoggerProvider provider in root.GetServices<ILoggerProvider>().OfType<CoreLoggerProvider>())
        { Attach(provider.MetricsSource); }
        root.GetService<ConfigurationRuntime>()?.AttachMetrics(Runtime);
        root.GetService<ITypeManager>()?.PreparationReport.AttachMetrics(Runtime);
        if (restComposition is not null)
        {
            IRestClientRuntime owner = root.GetRequiredService<IRestClientRuntime>();
            if (!ReferenceEquals(owner.RegistrationIdentity, restComposition.RegistrationIdentity))
            { throw new InvalidOperationException("The configured REST composition must belong to its exact registered runtime."); }
            owner.AttachMetrics(Runtime);
        }

        void Attach(IMetricsSource? source) => source?.AttachMetrics(Runtime);
    }

    // E closes branch admission through the same context before owner teardown; timeout does not settle callbacks.
    internal void BeginStopping()
    {
        lock (gate)
        {
            Volatile.Write(ref stopping, 1);
            HttpPolicy.BeginStopping();
            Runtime.BeginStopping();
        }
    }

    internal Task StopAsync()
    {
        BeginStopping();
        return Runtime.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        HttpPolicy.Dispose();
    }
}

internal sealed class DisabledMetrics : IMetrics
{
    internal static DisabledMetrics Instance { get; } = new();
    private static readonly NoopTimer Timer = new(TimeProvider.System);
    public ICounter CreateCounter(MetricDefinition definition) => NoopMetrics.Instance;
    public IUpDownCounter CreateUpDownCounter(MetricDefinition definition) => NoopMetrics.Instance;
    public IGauge CreateGauge(MetricDefinition definition) => NoopMetrics.Instance;
    public IHistogram CreateHistogram(MetricDefinition definition) => NoopMetrics.Instance;
    public ITimer CreateTimer(MetricDefinition definition) => Timer;
    public void RegisterObservableGauge(MetricDefinition definition, Func<IReadOnlyList<MetricObservation>> observe) { }
}
