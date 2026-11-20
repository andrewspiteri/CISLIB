using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CIS.RestClient;
using CIS.Workflows;
using CIS.Security.Hosting;
using global::OpenTelemetry.Metrics;

namespace CIS.Metrics.OpenTelemetry;

/// <summary>Installs the optional, exclusively configured CIS Metrics pipeline in the existing host.</summary>
public static class MetricsServiceCollectionExtensions
{
    /// <summary>Captures one finite profile and registers one provider/reader with authoritative identity.</summary>
    /// <remarks>Install after callback-owner lifecycle services and before Heartbeat/Startup. Existing OpenTelemetry tracing/logging can share the host;
    /// independent metrics configuration or global resource callbacks are unsupported. Changes require restart.</remarks>
    public static IServiceCollection AddCisMetrics(this IServiceCollection services, Action<MetricsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new MetricsOptions();
        configure?.Invoke(options);
        return AddCapturedCisMetrics(services, new MetricsSettings(options));
    }

    // The facade marker and ordinary API share exactly one installer and captured profile.
    internal static IServiceCollection AddCapturedCisMetrics(IServiceCollection services, MetricsSettings settings)
    {
        ServiceDescriptor? existing = services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(MetricsSettings));
        if (existing is not null)
        {
            if (existing.ImplementationInstance is not MetricsSettings previous || !previous.Equivalent(settings))
            {
                throw new InvalidOperationException("Repeated CIS Metrics installation must retain the same captured profile.");
            }
            if (settings.Enabled)
            {
                ((MetricsRegistrationGuard)services.Single(descriptor => descriptor.ServiceType == typeof(MetricsRegistrationGuard)).ImplementationInstance!).Validate();
            }
            return services;
        }
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IMetrics) || descriptor.ServiceType == typeof(MetricsCollectionContext)))
        {
            throw new InvalidOperationException("CIS Metrics cannot replace an existing Metrics facade or collection context.");
        }
        if (!settings.Enabled)
        {
            services.AddSingleton(settings);
            services.AddSingleton<IMetrics>(DisabledMetrics.Instance);
            return services;
        }
        // Reject unknown configuration before adding any owned descriptors.
        var guard = new MetricsRegistrationGuard(services);
        _ = new MetricsStreamCatalog(settings); // Validate the known catalog before mutating services.
        ServiceDescriptor[] precedingHosted = services.Where(descriptor => !descriptor.IsKeyedService
            && descriptor.ServiceType == typeof(IHostedService)).ToArray();
        services.AddSingleton(settings);
        services.AddSingleton(guard);
        services.AddSingleton(new MetricsHostingOrder(services));
        services.AddSingleton(provider => new MetricsCollectionContext(provider, settings, guard,
            RestComposition(services), WorkflowComposition(services), StateMachineCompositionMetadata(services),
            SecurityCompositionMetadata(services)));
        services.AddSingleton<IMetrics>(provider => provider.GetRequiredService<MetricsCollectionContext>().Metrics);
        if (settings.CollectCoreModules)
        {
            services.AddSingleton(provider => provider.GetRequiredService<MetricsCollectionContext>().Runtime);
        }
        // This guard executes before any owned builder operation, and therefore before later foreign callbacks.
        services.ConfigureOpenTelemetryMeterProvider((_, _) => guard.Validate());
        services.AddOpenTelemetry().WithMetrics(builder => builder.AddPrometheusExporter(exporter => MetricsExporterPolicy.Configure(exporter, settings)));
        services.ConfigureOpenTelemetryMeterProvider((provider, builder) =>
        {
            MetricsCollectionContext context = provider.GetRequiredService<MetricsCollectionContext>();
            builder.SetResourceBuilder(context.Resource())
                .SetMaxMetricStreams(settings.MaxStreams)
                .SetExemplarFilter(ExemplarFilterType.AlwaysOff)
                .AddMeter("CIS.Metrics", "Microsoft.AspNetCore.Hosting")
                .AddView(context.Select);
            if (context.Settings.CollectOutgoingHttp)
            {
                builder.AddMeter("System.Net.Http");
            }
        });
        services.AddSingleton<IHostedService, MetricsProviderActivation>();
        // Preserve the existing Core bridge tail, including when Metrics is installed after it.
        // Only descriptors added by this invocation move; no foreign service is reordered.
        ServiceDescriptor? bridge = precedingHosted.FirstOrDefault(descriptor =>
            descriptor.ImplementationType == typeof(CIS.Workflows.Hosting.PipelineHostedService)
            || descriptor.ImplementationType == typeof(CIS.RestClient.Hosting.RestHostedService)
            || descriptor.ImplementationType == typeof(CIS.Heartbeat.Hosting.HeartbeatHostedService)
            || descriptor.ImplementationType == typeof(CIS.Startup.Hosting.StartupHostedService));
        if (bridge is not null)
        {
            ServiceDescriptor[] addedHosted = services.Where(descriptor => !descriptor.IsKeyedService
                && descriptor.ServiceType == typeof(IHostedService)
                && !precedingHosted.Any(previous => ReferenceEquals(previous, descriptor))).ToArray();
            foreach (ServiceDescriptor descriptor in addedHosted)
            {
                services.Remove(descriptor);
            }
            int insertion = services.IndexOf(bridge);
            foreach (ServiceDescriptor descriptor in addedHosted)
            {
                services.Insert(insertion++, descriptor);
            }
        }
        guard.Seal();
        return services;
    }

    private static RestClientComposition? RestComposition(IServiceCollection services)
    {
        ServiceDescriptor[] markers = services.Where(descriptor => !descriptor.IsKeyedService
            && descriptor.ServiceType == typeof(RestClientComposition)).ToArray();
        if (markers.Length == 0)
        {
            return null;
        }
        if (markers.Length != 1 || markers[0].ImplementationInstance is not RestClientComposition composition)
        {
            throw new InvalidOperationException("REST composition must have one passive registration-owned metadata instance.");
        }
        return composition;
    }

    private static WorkflowsComposition? WorkflowComposition(IServiceCollection services)
    {
        ServiceDescriptor[] markers = services.Where(descriptor => !descriptor.IsKeyedService
            && descriptor.ServiceType == typeof(WorkflowsComposition)).ToArray();
        if (markers.Length == 0)
        {
            return null;
        }

        if (markers.Length != 1 || markers[0].ImplementationInstance is not WorkflowsComposition composition)
        {
            throw new InvalidOperationException("Workflow composition must have one passive registration-owned metadata instance.");
        }

        return composition;
    }

    private static StateMachineComposition? StateMachineCompositionMetadata(IServiceCollection services)
    {
        ServiceDescriptor[] markers = services.Where(descriptor => !descriptor.IsKeyedService
            && descriptor.ServiceType == typeof(StateMachineComposition)).ToArray();
        if (markers.Length == 0)
        {
            return null;
        }

        if (markers.Length != 1 || markers[0].ImplementationInstance is not StateMachineComposition composition)
        {
            throw new InvalidOperationException("State-machine composition must have one passive registration-owned metadata instance.");
        }

        return composition;
    }

    private static SecurityComposition? SecurityCompositionMetadata(IServiceCollection services)
    {
        ServiceDescriptor[] markers = services.Where(descriptor => !descriptor.IsKeyedService
            && descriptor.ServiceType == typeof(SecurityComposition)).ToArray();
        if (markers.Length == 0)
        {
            return null;
        }

        if (markers.Length != 1 || markers[0].ImplementationInstance is not SecurityComposition composition)
        {
            throw new InvalidOperationException("Security composition must have one passive registration-owned metadata instance.");
        }

        return composition;
    }
}
