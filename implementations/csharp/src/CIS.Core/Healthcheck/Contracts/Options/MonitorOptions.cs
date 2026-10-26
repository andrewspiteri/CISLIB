using CIS.Text;

namespace CIS.Healthcheck;

/// <summary>Explicit owner-selected identity and criticality for one monitor lifetime.</summary>
public abstract class HealthMonitorOptions
{
    /// <summary>Gets whether an unhealthy instance contributes to aggregate unhealthy state.</summary>
    public required bool IsCritical { get; init; }
    /// <summary>Gets optional display metadata, limited to 128 Unicode scalar values.</summary>
    public string? Name { get; init; }

    internal void ValidateCommon()
    {
        if (Name is not null && !UnicodeScalarText.IsValid(Name, 128, allowControls: true))
        {
            throw new ArgumentException("Use a display name of at most 128 valid Unicode scalar values.", nameof(Name));
        }
    }
}

/// <summary>Settings for an owner-flagged explicit condition.</summary>
public sealed class ConditionMonitorOptions : HealthMonitorOptions
{
    /// <summary>Gets the required initial flag; clear evaluates Healthy and flagged evaluates Unhealthy.</summary>
    public required bool InitiallyFlagged { get; init; }
    /// <summary>Gets the optional detached initial reason.</summary>
    public HealthDiagnostic? InitialDiagnostic { get; init; }
}

/// <summary>Settings for a policy that flags on the Nth consecutive failed outcome.</summary>
public sealed class ConsecutiveFailureMonitorOptions : HealthMonitorOptions
{
    /// <summary>Gets the required threshold, from one through Int32.MaxValue.</summary>
    public required int FailureThreshold { get; init; }
}

/// <summary>Settings for an explicit finite numeric breach comparison.</summary>
public sealed class NumericThresholdMonitorOptions : HealthMonitorOptions
{
    /// <summary>Gets the required comparison defining a breach.</summary>
    public required NumericComparison Comparison { get; init; }
    /// <summary>Gets the required finite threshold.</summary>
    public required double Threshold { get; init; }
    /// <summary>Gets the required nonblank unit, limited to 32 Unicode scalar values.</summary>
    public required string Unit { get; init; }
}

/// <summary>Settings for availability established by usable-data publication.</summary>
public sealed class AvailabilityMonitorOptions : HealthMonitorOptions
{
}
