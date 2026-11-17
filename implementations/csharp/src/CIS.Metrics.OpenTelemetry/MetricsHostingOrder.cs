using CIS.Heartbeat.Hosting;
using CIS.Startup.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CIS.Metrics.OpenTelemetry;

// Inspect final ordinary descriptors without resolving the hosted graph again.
// Core bridges supply their own final-tail validation and callback barrier.
internal sealed class MetricsHostingOrder(IServiceCollection services)
{
    private static readonly Type? WebHost = typeof(GenericHostWebHostBuilderExtensions).Assembly
        .GetType("Microsoft.AspNetCore.Hosting.GenericWebHostService");

    internal void Validate()
    {
        ServiceDescriptor[] hosted = services.Where(item => !item.IsKeyedService
            && item.ServiceType == typeof(IHostedService)).ToArray();
        int activation = Array.FindIndex(hosted, item => item.ImplementationType == typeof(MetricsProviderActivation));
        if (activation < 0 || hosted.Count(item => item.ImplementationType == typeof(MetricsProviderActivation)) != 1)
        { throw new InvalidOperationException("Install one Metrics activation service in the existing host."); }
        int bridge = Array.FindIndex(hosted, item => item.ImplementationType == typeof(HeartbeatHostedService)
            || item.ImplementationType == typeof(StartupHostedService));
        if (bridge >= 0)
        {
            if (activation >= bridge)
            { throw new InvalidOperationException("Install Metrics activation before the existing Heartbeat/Startup shutdown bridge."); }
            return;
        }
        bool final = activation == hosted.Length - 1
            || activation == hosted.Length - 2 && WebHost is not null && hosted[^1].ImplementationType == WebHost;
        if (!final)
        { throw new InvalidOperationException("Without a Heartbeat/Startup bridge, install Metrics after callback-owner hosted services; only the framework web host may follow it."); }
    }
}
