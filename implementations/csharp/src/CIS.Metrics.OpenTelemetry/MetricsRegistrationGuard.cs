using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using global::OpenTelemetry.Exporter;
using global::OpenTelemetry.Metrics;

namespace CIS.Metrics.OpenTelemetry;

// One pinned SDK registration boundary. It inspects ordinary descriptors, never private SDK state or factories.
internal sealed class MetricsRegistrationGuard
{
    private readonly IServiceCollection services;
    private readonly Type configurationType;
    private ServiceDescriptor[]? owned;

    internal MetricsRegistrationGuard(IServiceCollection services)
    {
        this.services = services;
        var probe = new ServiceCollection();
        probe.ConfigureOpenTelemetryMeterProvider(static (_, _) => { });
        if (probe.Count != 1 || probe[0].ImplementationInstance is null || probe[0].Lifetime != ServiceLifetime.Singleton)
        { throw new NotSupportedException("The pinned OpenTelemetry registration route changed; Metrics cannot establish exclusive configuration ownership."); }
        configurationType = probe[0].ServiceType;
        if (services.Any(descriptor => Protected(descriptor.ServiceType)))
        { throw new InvalidOperationException("Install CIS Metrics before independently configured metric providers, readers, views, exporter options or global OpenTelemetry resource callbacks."); }
    }

    internal void Seal() => owned = services.Where(descriptor => Protected(descriptor.ServiceType)).ToArray();

    internal void Validate()
    {
        ServiceDescriptor[] current = services.Where(descriptor => Protected(descriptor.ServiceType)).ToArray();
        if (owned is null || current.Length != owned.Length || current.Where((descriptor, index) => !ReferenceEquals(descriptor, owned[index])).Any())
        { throw new InvalidOperationException("CIS Metrics owns its provider, reader, views, resources and exporter options; external metric configuration additions or replacements are unsupported."); }
    }

    private bool Protected(Type type)
        => type == configurationType || type == typeof(MeterProvider) || type == typeof(IMetrics)
            || type == typeof(MetricsCollectionContext) || type == typeof(MetricsSettings) || type == typeof(MetricsRegistrationGuard)
            || type == typeof(MetricsRuntime)
            || type == typeof(IConfigureOptions<PrometheusAspNetCoreOptions>)
            || type == typeof(IPostConfigureOptions<PrometheusAspNetCoreOptions>)
            || type == typeof(IValidateOptions<PrometheusAspNetCoreOptions>)
            || type == typeof(IOptionsChangeTokenSource<PrometheusAspNetCoreOptions>)
            || type == typeof(IOptionsFactory<PrometheusAspNetCoreOptions>)
            || type == typeof(IOptionsMonitor<PrometheusAspNetCoreOptions>)
            || type == typeof(IOptions<PrometheusAspNetCoreOptions>);
}
