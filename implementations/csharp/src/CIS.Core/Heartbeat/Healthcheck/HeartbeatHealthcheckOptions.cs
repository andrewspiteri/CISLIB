using CIS.Healthcheck;

namespace CIS.Heartbeat.Healthcheck;

/// <summary>Selects exactly one existing Healthcheck monitor or typed policy for a Heartbeat registration.</summary>
/// <remarks>Absence of this integration creates no monitor. Healthcheck alone interprets policy and counts outcomes.</remarks>
public sealed class HeartbeatHealthcheckOptions
{
    /// <summary>Gets an existing Core consecutive-failure or availability monitor, which remains caller-owned.</summary>
    public IHealthMonitor? Monitor { get; init; }
    /// <summary>Gets a consecutive-failure policy whose monitor is owned by the binding.</summary>
    public ConsecutiveFailureMonitorOptions? ConsecutiveFailures { get; init; }
    /// <summary>Gets an availability policy whose monitor is owned by the binding.</summary>
    public AvailabilityMonitorOptions? Availability { get; init; }
    /// <summary>Gets the Core factory required when selecting a typed policy.</summary>
    public IHealthMonitorFactory? MonitorFactory { get; init; }
    /// <summary>Gets the explicit declaration required for availability: a successful callback publishes usable data.</summary>
    public bool SuccessPublishesUsableData { get; init; }
}
