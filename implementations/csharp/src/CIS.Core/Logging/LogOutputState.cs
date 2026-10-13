namespace CIS.Logging;

/// <summary>Describes the lifecycle of one enabled logging output.</summary>
public enum LogOutputState
{
    /// <summary>Accepting work and processing admitted records.</summary>
    Running = 0,
    /// <summary>Terminally faulted; recovery requires a new runtime.</summary>
    Faulted = 1,
    /// <summary>Admission is closed while the worker resolves existing work.</summary>
    Stopping = 2,
    /// <summary>The worker has finished. Retained fault information may still exist.</summary>
    Stopped = 3
}
