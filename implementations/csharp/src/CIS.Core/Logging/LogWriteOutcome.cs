namespace CIS.Logging;

/// <summary>The aggregate admission outcome; acceptance is not delivery confirmation.</summary>
public enum LogWriteOutcome : int
{
    /// <summary>The runtime accepted the captured event into its incoming queue. This is not writer admission or delivery confirmation.</summary>
    Accepted = 0,
    /// <summary>Some interested outputs accepted and others rejected.</summary>
    PartiallyAccepted = 1,
    /// <summary>No configured output was interested.</summary>
    Filtered = 2,
    /// <summary>No interested output accepted.</summary>
    Rejected = 3,
    /// <summary>Runtime admission is closed.</summary>
    Stopped = 4,
    /// <summary>Event capture failed before output admission.</summary>
    InvalidEvent = 5
}

/// <summary>The admission result for one enabled output.</summary>
public enum LogOutputWriteStatus : int
{
    /// <summary>Accepted by this output.</summary>
    Accepted = 0,
    /// <summary>Below this output's threshold.</summary>
    Filtered = 1,
    /// <summary>This output rejected the incoming event because its queue was full.</summary>
    QueueFull = 2,
    /// <summary>This output is faulted.</summary>
    Faulted = 3,
    /// <summary>Runtime admission is closed.</summary>
    Stopped = 4
}
