using CIS.Security;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Audit.Hosting;

/// <summary>Explicitly selects the Security bridge without constructing services or performing I/O.</summary>
public static class AuditSecurityBridgeServiceCollectionExtensions
{
    /// <summary>Selects one bridge before AddCisSecurity. Configure Audit capture and a writer through their existing composition.</summary>
    public static IServiceCollection AddCisSecurityAuditBridge(this IServiceCollection services,
        AuditSecurityBridgeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ISecurityAuditSink) && !descriptor.IsKeyedService)
            || services.Any(descriptor => descriptor.ServiceType == typeof(AuditSecurityBridge)))
        {
            throw new InvalidOperationException("Select the Audit Security bridge before AddCisSecurity and without an existing unkeyed audit sink.");
        }

        AuditSecurityBridgeOptions selected = options ?? new();
        services.AddSingleton(selected);
        services.AddSingleton(provider => new AuditSecurityBridge(provider.GetRequiredService<AuditCaptureEngine>(),
            provider.GetRequiredService<IAuditWriter>(), selected, provider.GetService<TimeProvider>() ?? TimeProvider.System));
        services.AddSingleton<ISecurityAuditSink>(provider => provider.GetRequiredService<AuditSecurityBridge>());
        return services;
    }
}
