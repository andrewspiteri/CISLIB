using CIS.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Metrics.OpenTelemetry;

/// <summary>Collects the optional Metrics profile for automatic CISApplication composition.</summary>
public static class CISApplicationMetricsBuilderExtensions
{
    /// <summary>Captures one immutable profile without resolving services, constructing a provider or starting collection.</summary>
    /// <remarks>The installed optional integration consumes this profile during preparation.
    /// Disable collection with Enabled=false. EnableOptionalIntegrations=false disables automatic package installation.</remarks>
    public static CISApplicationBuilder ConfigureCisMetrics(this CISApplicationBuilder builder, Action<MetricsOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new MetricsOptions();
        configure(options);
        var settings = new MetricsSettings(options);
        ServiceDescriptor[] existing = builder.Services.Where(descriptor => !descriptor.IsKeyedService
            && descriptor.ServiceType == typeof(CISApplicationMetricsProfile)).ToArray();
        if (existing.Length != 0)
        {
            if (existing.Length != 1 || existing[0].ImplementationInstance is not CISApplicationMetricsProfile previous
                || !previous.Settings.Equivalent(settings))
            { throw new InvalidOperationException("Repeated CISApplication Metrics setup must retain the same captured profile."); }
            return builder;
        }
        builder.Services.AddSingleton(new CISApplicationMetricsProfile(settings));
        return builder;
    }
}

// Passive known registration metadata. Caller options and delegates are not retained.
internal sealed record CISApplicationMetricsProfile(MetricsSettings Settings);
