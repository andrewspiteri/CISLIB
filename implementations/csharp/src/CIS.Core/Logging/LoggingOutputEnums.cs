namespace CIS.Logging;

/// <summary>The output representation.</summary>
public enum LogOutputFormat
{
    /// <summary>Human.</summary>
    Human = 0,
    /// <summary>JsonLines.</summary>
    JsonLines = 1,
}

/// <summary>The selected standard console stream.</summary>
public enum ConsoleOutputStream
{
    /// <summary>StandardError.</summary>
    StandardError = 0,
    /// <summary>StandardOutput.</summary>
    StandardOutput = 1,
}

/// <summary>The human output detail level.</summary>
public enum HumanOutputDetail
{
    /// <summary>Normal.</summary>
    Normal = 0,
    /// <summary>Verbose.</summary>
    Verbose = 1,
}
