using CIS.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Metrics.OpenTelemetry;

/// <summary>Connects the existing optional collector to CISApplication's single provider and operational pipeline.</summary>
/// <remarks>Discovery uses the already-prepared Type Manager catalog. Registration and construction perform no I/O or service resolution.</remarks>
public sealed class MetricsApplicationIntegration : ICISApplicationIntegration
{
    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        ServiceDescriptor[] profiles = services.Where(descriptor => !descriptor.IsKeyedService
            && descriptor.ServiceType == typeof(CISApplicationMetricsProfile)).ToArray();
        if (profiles.Length != 0)
        {
            if (profiles.Length != 1 || profiles[0].ImplementationInstance is not CISApplicationMetricsProfile profile)
            { throw new InvalidOperationException("CISApplication Metrics setup requires one captured profile."); }
            MetricsServiceCollectionExtensions.AddCapturedCisMetrics(services, profile.Settings);
            return;
        }
        ServiceDescriptor? installed = services.FirstOrDefault(descriptor => !descriptor.IsKeyedService
            && descriptor.ServiceType == typeof(MetricsSettings));
        if (installed is not null)
        {
            if (installed.ImplementationInstance is not MetricsSettings settings)
            { throw new InvalidOperationException("CIS Metrics requires its known captured profile."); }
            MetricsServiceCollectionExtensions.AddCapturedCisMetrics(services, settings);
            return;
        }
        // A supported explicit facade replacement belongs to the application.
        if (services.Any(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(IMetrics)))
        { return; }
        services.AddCisMetrics();
    }

    /// <inheritdoc />
    public void ConfigureWeb(WebApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        // Manual facade replacements have no collector branch; disabled profiles use the existing no-op path.
        if (application.Services.GetService<MetricsSettings>() is not null) { application.UseCisMetrics(); }
    }
}
