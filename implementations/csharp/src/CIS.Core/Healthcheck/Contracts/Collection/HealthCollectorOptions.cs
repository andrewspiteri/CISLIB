namespace CIS.Healthcheck;

/// <summary>Registration input copied and validated before a collector is attached.</summary>
public sealed class HealthCollectorOptions
{
    /// <summary>Gets the optional positive recurrence interval. Null selects manual collection only.</summary>
    public TimeSpan? Interval { get; init; }

    /// <summary>Gets whether evaluations must provide a result matching the associated monitor's policy.</summary>
    public bool IsRequired { get; init; } = true;
}
