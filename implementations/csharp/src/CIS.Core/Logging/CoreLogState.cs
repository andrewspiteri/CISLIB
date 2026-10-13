namespace CIS.Logging;

/// <summary>Describes admission and worker draining for a logging runtime.</summary>
public enum CoreLogState
{
    /// <summary>Runtime admission is open; individual outputs may be faulted.</summary>
    Running = 0,
    /// <summary>Admission is closed and an output worker has not finished.</summary>
    Stopping = 1,
    /// <summary>Admission is closed and all output workers have finished.</summary>
    Stopped = 2
}
