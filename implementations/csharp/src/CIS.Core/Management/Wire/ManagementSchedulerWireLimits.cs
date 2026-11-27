namespace CIS.Management.Wire;

/// <summary>Stricter-only scheduler framing limits; existing codecs and their limits remain unchanged.</summary>
public sealed class ManagementSchedulerWireLimits
{
    /// <summary>Captures positive finite limits within the published first-release ceilings.</summary>
    public ManagementSchedulerWireLimits(int maximumBodyBytes = ManagementSchedulerLimits.MaximumHttpBytes,
        int maximumPayloadBytes = ManagementSchedulerLimits.MaximumPayloadBytes,
        int maximumCapsuleBytes = ManagementSchedulerLimits.MaximumCapsuleBytes,
        int maximumSummaryBytes = ManagementSchedulerLimits.MaximumSummaryBytes,
        int maximumJsonDepth = ManagementSchedulerLimits.MaximumJsonDepth,
        int maximumPageSize = ManagementSchedulerLimits.MaximumPageSize)
    {
        MaximumBodyBytes = Bound(maximumBodyBytes, ManagementSchedulerLimits.MaximumHttpBytes, nameof(maximumBodyBytes));
        MaximumPayloadBytes = Bound(maximumPayloadBytes, ManagementSchedulerLimits.MaximumPayloadBytes, nameof(maximumPayloadBytes));
        MaximumCapsuleBytes = Bound(maximumCapsuleBytes, ManagementSchedulerLimits.MaximumCapsuleBytes, nameof(maximumCapsuleBytes));
        MaximumSummaryBytes = Bound(maximumSummaryBytes, ManagementSchedulerLimits.MaximumSummaryBytes, nameof(maximumSummaryBytes));
        MaximumJsonDepth = Bound(maximumJsonDepth, ManagementSchedulerLimits.MaximumJsonDepth, nameof(maximumJsonDepth));
        MaximumPageSize = Bound(maximumPageSize, ManagementSchedulerLimits.MaximumPageSize, nameof(maximumPageSize));
    }

    /// <summary>Gets the maximum complete encoded HTTP body bytes.</summary>
    public int MaximumBodyBytes
    {
        get;
    }
    /// <summary>Gets the maximum decoded UTF8 input/event/business-settings bytes.</summary>
    public int MaximumPayloadBytes
    {
        get;
    }
    /// <summary>Gets the maximum complete encoded execution capsule bytes.</summary>
    public int MaximumCapsuleBytes
    {
        get;
    }
    /// <summary>Gets the maximum decoded result-summary bytes.</summary>
    public int MaximumSummaryBytes
    {
        get;
    }
    /// <summary>Gets the strict JSON depth bound.</summary>
    public int MaximumJsonDepth
    {
        get;
    }
    /// <summary>Gets the maximum complete response page item count.</summary>
    public int MaximumPageSize
    {
        get;
    }

    private static int Bound(int value, int ceiling, string name)
        => value >= 1 && value <= ceiling ? value
            : throw new ArgumentOutOfRangeException(name, "Select a positive bound within the published scheduler ceiling.");
}

/// <summary>The actual resource family selected by a typed scheduler state route.</summary>
public enum ManagementSchedulerStateTarget
{
    /// <summary>A configured trigger, which has no task activation operation.</summary>
    Trigger,
    /// <summary>A Scheduled Task, including guarded revision activation.</summary>
    Task,
    /// <summary>A logical run's explicit cancellation request.</summary>
    Run
}
