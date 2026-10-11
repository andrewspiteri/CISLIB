namespace CIS.Logging;

/// <summary>The logging severity; None disables interest.</summary>
public enum CoreLogLevel
{
    /// <summary>Trace.</summary>
    Trace = 0,
    /// <summary>Debug.</summary>
    Debug = 1,
    /// <summary>Information.</summary>
    Information = 2,
    /// <summary>Warning.</summary>
    Warning = 3,
    /// <summary>Error.</summary>
    Error = 4,
    /// <summary>Critical.</summary>
    Critical = 5,
    /// <summary>None.</summary>
    None = 6,
}
