using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CIS.RestClient.Resilience;

/// <summary>Registers the optional retry executor without adding an HTTP retry handler.</summary>
public static class RestClientResilienceServiceCollectionExtensions
{
    /// <summary>Registers one through thirty-two explicit distinct immutable policies and one host-owned executor.</summary>
    /// <remarks>Install once. A policy is available by name; registration does not select it for a client.
    /// The executor and operation owner must use the same root TimeProvider, defaulting to TimeProvider.System.</remarks>
    public static IServiceCollection AddCisRestClientResilience(this IServiceCollection services, params RestRetryPolicy[] policies)
    {
        ArgumentNullException.ThrowIfNull(services);
        RestRetryPolicy[] copied = PollyRestRetryExecutor.CopyPolicies(policies);
        if (services.Any(item => item.ServiceType == typeof(IRestRetryExecutor) || item.ServiceType == typeof(RestRetryPolicy)))
        { throw new InvalidOperationException("Register the explicit RestClient retry policies and executor once, without replacing another registry."); }
        foreach (RestRetryPolicy policy in copied) { services.AddSingleton(policy); }
        services.AddSingleton<IRestRetryExecutor>(root => new PollyRestRetryExecutor(copied,
            root.GetService<TimeProvider>() ?? TimeProvider.System, root.GetService<ILogger<PollyRestRetryExecutor>>()));
        return services;
    }
}
