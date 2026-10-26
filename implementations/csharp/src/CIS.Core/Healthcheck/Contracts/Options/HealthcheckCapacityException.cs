namespace CIS.Healthcheck;

/// <summary>Reports atomic rejection of a monitor registration at the configured capacity.</summary>
public sealed class HealthcheckCapacityException : InvalidOperationException
{
    /// <summary>Creates a capacity error without retaining application state.</summary>
    /// <param name="limit">The positive configured monitor capacity.</param>
    public HealthcheckCapacityException(int limit)
        : base("The Healthcheck monitor capacity has been reached.")
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        Limit = limit;
    }

    /// <summary>Gets the configured capacity.</summary>
    public int Limit { get; }
    /// <summary>Gets the stable failure code.</summary>
    public string Code => "Healthcheck.MonitorCapacity";
}
