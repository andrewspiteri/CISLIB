namespace CIS.Logging;

/// <summary>The aggregate admission outcome; acceptance is not delivery confirmation.</summary>
public enum LogWriteOutcome : int
{
    /// <summary>The public runtime accepted the captured event into its incoming queue. Internal output-stage qualification uses this for admission by every interested output. Neither confirms delivery.</summary>
    Accepted = 0,
    /// <summary>Internal output-stage qualification: some interested outputs accepted and others rejected. Public incoming admission does not return this outcome.</summary>
    PartiallyAccepted = 1,
    /// <summary>No configured output was interested.</summary>
    Filtered = 2,
    /// <summary>Incoming admission was rejected, or no interested output accepted on an early or internal output-stage path.</summary>
    Rejected = 3,
    /// <summary>Runtime admission is closed.</summary>
    Stopped = 4,
    /// <summary>Event capture failed before output admission.</summary>
    InvalidEvent = 5
}

/// <summary>The admission result for one enabled output.</summary>
public enum LogOutputWriteStatus : int
{
    /// <summary>Internal output-stage qualification: accepted by this output. Public incoming acceptance has no output entries.</summary>
    Accepted = 0,
    /// <summary>Below this output's threshold.</summary>
    Filtered = 1,
    /// <summary>Internal output-stage qualification: this output rejected admission because its queue was full. Public incoming queue rejection has no output entries.</summary>
    QueueFull = 2,
    /// <summary>This output is faulted.</summary>
    Faulted = 3,
    /// <summary>Runtime admission is closed.</summary>
    Stopped = 4
}
