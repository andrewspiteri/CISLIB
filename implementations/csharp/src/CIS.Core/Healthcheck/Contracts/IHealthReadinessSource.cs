namespace CIS.Healthcheck;

/// <summary>An optional local readiness prerequisite, independent of liveness and existing critical monitors.</summary>
/// <remarks>Read retained bounded facts only. The property performs no I/O, waits or collector execution.
/// A false observation closes readiness without revoking admitted work or changing process health.</remarks>
public interface IHealthReadinessSource
{
    /// <summary>Gets whether this prerequisite currently permits readiness.</summary>
    bool IsReady
    {
        get;
    }
}
