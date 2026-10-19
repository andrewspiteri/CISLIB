using CIS.Configuration;

namespace CIS.Logging;

public sealed partial class CoreLogRuntime
{
    /// <summary>Starts logging with explicit recoverable failure results and an application-owned runtime handle.</summary>
    public static LogStartResult TryCreate(out CoreLogRuntime? runtime)
    {
        runtime = null;
        try { runtime = Create(); return new(true); }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
        { return StartupFailure(error); }
    }

    /// <summary>Starts logging with captured application writers and explicit recoverable failure results.</summary>
    public static LogStartResult TryCreate(IEnumerable<LogWriterRegistration> writers, out CoreLogRuntime? runtime)
    {
        runtime = null;
        try { runtime = Create(writers); return new(true); }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
        { return StartupFailure(error); }
    }

    /// <summary>Starts logging with captured policies/writers and explicit recoverable failure results.</summary>
    public static LogStartResult TryCreate(LogProcessingOptions processing, out CoreLogRuntime? runtime,
        IEnumerable<LogWriterRegistration>? writers = null)
    {
        runtime = null;
        try { runtime = Create(processing, writers); return new(true); }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
        { return StartupFailure(error); }
    }

    private static LogStartResult StartupFailure(Exception error) => new(false, error switch
    {
        CoreConfigurationException => "StartupConfigurationInvalid",
        CoreLogInitializationException initialization => initialization.ErrorCode,
        ArgumentException => "InvalidStartupInput",
        _ => "StartupFailed"
    });
}
