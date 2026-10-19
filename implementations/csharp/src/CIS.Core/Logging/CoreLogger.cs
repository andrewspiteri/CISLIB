namespace CIS.Logging;

/// <summary>A component logger borrowing its runtime; it has no independent lifecycle.</summary>
/// <remarks>Methods support concurrent calls. Method severity alone controls filtering and capture.</remarks>
public sealed partial class CoreLogger
{
    private readonly CoreLogRuntime runtime;
    private readonly string component;

    internal CoreLogger(CoreLogRuntime runtime, string component)
    {
        this.runtime = runtime;
        this.component = component;
    }

    /// <summary>Checks running output interest without reserving queue capacity.</summary>
    /// <remarks>Returns false in processing callback flows, including inherited child tasks across runtimes.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The level is undefined.</exception>
    public bool IsEnabled(CoreLogLevel level) => runtime.IsEnabled(level);

    /// <summary>Captures owned event facts once when interested, then attempts nonblocking incoming admission.</summary>
    /// <exception cref="ArgumentNullException">Data is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The level is undefined.</exception>
    public LogWriteResult Write(CoreLogLevel level, LogEventData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return runtime.Write(component, level, data, null);
    }

    /// <summary>Invokes the factory at most once after early interest checks.</summary>
    /// <remarks>Public frontend runtimes contain recoverable factory exceptions as FactoryFailed results. Null results are InvalidEvent. The callback stays on the caller.</remarks>
    /// <exception cref="ArgumentNullException">The factory is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The level is undefined.</exception>
    public LogWriteResult WriteLazy(CoreLogLevel level, Func<LogEventData> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return runtime.Write(component, level, null, factory);
    }
}
