using CIS.Healthcheck;

namespace CIS.Heartbeat.Healthcheck;

/// <summary>Selects exactly one existing Healthcheck monitor or typed policy for a Heartbeat registration.</summary>
/// <remarks>Absence of this integration creates no monitor. The selected public registration capability determines
/// supported monitor providers. Production binds suitable Core monitors and delegates policy/counts to Healthcheck.</remarks>
public sealed class HeartbeatHealthcheckOptions
{
    /// <summary>Gets an existing consecutive-failure or availability monitor, which remains caller-owned.</summary>
    /// <remarks>The production scheduler requires a suitable Core monitor; public substitutes may support recording monitors.</remarks>
    public IHealthMonitor? Monitor
    {
        get; init;
    }
    /// <summary>Gets a consecutive-failure policy whose monitor is owned by the binding.</summary>
    public ConsecutiveFailureMonitorOptions? ConsecutiveFailures
    {
        get; init;
    }
    /// <summary>Gets an availability policy whose monitor is owned by the binding.</summary>
    public AvailabilityMonitorOptions? Availability
    {
        get; init;
    }
    /// <summary>Gets the factory required by the selected capability when selecting a typed policy.</summary>
    /// <remarks>The production scheduler requires the Core factory for atomic policy binding.</remarks>
    public IHealthMonitorFactory? MonitorFactory
    {
        get; init;
    }
    /// <summary>Gets the explicit declaration required for availability: a successful callback publishes usable data.</summary>
    public bool SuccessPublishesUsableData
    {
        get; init;
    }
}
