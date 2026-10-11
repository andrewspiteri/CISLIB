namespace CIS;

/// <summary>The application execution kind selected explicitly at startup.</summary>
public enum EnvironmentKind
{
    /// <summary>Unknown.</summary>
    Unknown = 0,
    /// <summary>Console.</summary>
    Console = 1,
    /// <summary>Service.</summary>
    Service = 2,
    /// <summary>Web.</summary>
    Web = 3,
    /// <summary>Function.</summary>
    Function = 4,
}
