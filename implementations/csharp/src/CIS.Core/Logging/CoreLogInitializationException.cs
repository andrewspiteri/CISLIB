namespace CIS.Logging;

/// <summary>A safe output initialization failure; the original exception and payload are not retained.</summary>
public sealed class CoreLogInitializationException : Exception
{
    internal CoreLogInitializationException(string outputName, string errorCode)
        : base("Logging output '" + outputName + "' could not initialize (" + errorCode + ").")
    {
        OutputName = outputName; ErrorCode = errorCode;
    }
    /// <summary>The fixed destination name.</summary>
    public string OutputName { get; }
    /// <summary>The fixed initialization category; never a raw payload or configured path.</summary>
    public string ErrorCode { get; }
}
