namespace CIS.Logging;

/// <summary>A component logger borrowing its runtime; it has no independent lifecycle.</summary>
/// <remarks>Methods support concurrent calls. Method severity alone controls filtering and capture.</remarks>
public sealed class CoreLogger
{
    private readonly CoreLogRuntime runtime;
    private readonly string component;

    internal CoreLogger(CoreLogRuntime runtime, string component)
    {
        this.runtime = runtime;
        this.component = component;
    }

    /// <summary>Checks running output interest without reserving queue capacity.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The level is undefined.</exception>
    public bool IsEnabled(CoreLogLevel level) => runtime.IsEnabled(level);

    /// <summary>Captures once when interested, then attempts independent nonblocking admission.</summary>
    /// <exception cref="ArgumentNullException">Data is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The level is undefined.</exception>
    public LogWriteResult Write(CoreLogLevel level, LogEventData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return runtime.Write(component, level, data, null);
    }

    /// <summary>Invokes the factory at most once after early interest checks.</summary>
    /// <remarks>A caller factory exception propagates unchanged. Null factory results are InvalidEvent.</remarks>
    /// <exception cref="ArgumentNullException">The factory is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The level is undefined.</exception>
    public LogWriteResult WriteLazy(CoreLogLevel level, Func<LogEventData> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return runtime.Write(component, level, null, factory);
    }
}
