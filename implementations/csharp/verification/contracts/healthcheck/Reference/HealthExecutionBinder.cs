// HC00 nonshipping facade. Never execute this type as runtime evidence.
namespace CIS.Healthcheck;

/// <summary>Compile-only shape of the execution-fact binding entry points.</summary>
public static class HealthExecutionBinder
{
    public static IHealthExecutionBinding Bind(
        IHealthMonitor monitor,
        HealthExecutionBindingOptions options) => throw ReferenceOnly.Unavailable();

    public static IHealthExecutionBinding Bind(
        IHealthMonitorFactory factory,
        ConsecutiveFailureMonitorOptions policy,
        HealthExecutionBindingOptions options) => throw ReferenceOnly.Unavailable();

    public static IHealthExecutionBinding Bind(
        IHealthMonitorFactory factory,
        AvailabilityMonitorOptions policy,
        HealthExecutionBindingOptions options) => throw ReferenceOnly.Unavailable();
}
