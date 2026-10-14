using Microsoft.Extensions.Logging;

namespace CIS.Logging.MicrosoftLogger;

/// <summary>Creates Microsoft category loggers that borrow an application-owned core runtime.</summary>
/// <remarks>Disposal disables this provider's future calls and never stops or disposes the shared runtime.
/// A call already running when disposal occurs may finish. Category names follow the core component rules.
/// Supplied rendered messages and exceptions enter core capture. Null or empty EventId.Name becomes
/// log.message; EventId.Id supplies the integer attribute event_id, including zero and negative values.
/// Structured state preserves {OriginalFormat} without parsing. Core checks interest before invoking
/// formatter or copying state; full queues remain interested. Formatter exceptions propagate unchanged;
/// invalid retained payload increments core diagnostics without output admission. Oversized valid payload
/// is truncated. Disposed, stopped, filtered and all-faulted calls skip both callbacks after argument guards.
/// Healthy outputs admit independently. Provider-created scopes are copied at BeginScope; borrowed host
/// scopes are copied during accepted capture, after Activity and clock snapshots. Combined scope depth is 32.</remarks>
public sealed class CoreLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly CoreLogRuntime runtime;
    private int disposed;
    private readonly object scopeProviderGate = new();
    private IExternalScopeProvider? externalScopes;

    /// <summary>Creates a nonowning provider over an existing runtime.</summary>
    /// <exception cref="ArgumentNullException">Runtime is null.</exception>
    public CoreLoggerProvider(CoreLogRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        this.runtime = runtime;
    }

    /// <summary>Creates a category logger using the core's validated component name.</summary>
    /// <exception cref="ObjectDisposedException">This provider has been disposed.</exception>
    /// <exception cref="ArgumentNullException">Category name is null.</exception>
    /// <exception cref="ArgumentException">The name does not satisfy the core component rules.</exception>
    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return new CategoryLogger(this, runtime.CreateLogger(categoryName));
    }

    private bool IsDisposed => Volatile.Read(ref disposed) != 0;

    /// <summary>Idempotently disables this provider; the application retains runtime ownership.</summary>
    public void Dispose()
    {
        lock (scopeProviderGate) { Interlocked.Exchange(ref disposed, 1); Volatile.Write(ref externalScopes, null); }
    }

    /// <summary>Borrows the host scope source; accepted events copy its active scopes inline.</summary>
    /// <remarks>Existing core/provider scopes are the outer layer, host scopes the inner layer, and event attributes replace matching retained keys.
    /// Host scope lifetime belongs to the host. Provider disposal releases this reference and never disposes host handles.</remarks>
    /// <exception cref="ArgumentNullException">Scope provider is null.</exception>
    /// <exception cref="ObjectDisposedException">This provider is disposed.</exception>
    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeProvider);
        lock (scopeProviderGate) { ObjectDisposedException.ThrowIf(IsDisposed, this); Volatile.Write(ref externalScopes, scopeProvider); }
    }

    private static CoreLogLevel Level(LogLevel logLevel)
    {
        if (!Enum.IsDefined(logLevel)) { throw new ArgumentOutOfRangeException(nameof(logLevel)); }
        // The selected v1 Core and Microsoft severity numbers are identical (Trace through None).
        return (CoreLogLevel)logLevel;
    }

    private sealed class CategoryLogger(CoreLoggerProvider provider, CoreLogger core) : ILogger
    {
        public bool IsEnabled(LogLevel logLevel)
        {
            CoreLogLevel level = Level(logLevel);
            return !provider.IsDisposed && core.IsEnabled(level);
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            ArgumentNullException.ThrowIfNull(state);
            ObjectDisposedException.ThrowIf(provider.IsDisposed, provider);
            if (provider.runtime.IsAdmissionClosed) { throw new InvalidOperationException("Cannot begin a scope after runtime admission closes."); }
            return provider.runtime.Scopes.BeginCopiedScope(() => ProviderScopeCapture.Copy(state));
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            CoreLogLevel level = Level(logLevel);
            if (provider.IsDisposed) { return; }
            IExternalScopeProvider? scopes = Volatile.Read(ref provider.externalScopes);
            _ = core.WriteLazy(level, () => new LogEventData
            {
                EventName = string.IsNullOrEmpty(eventId.Name) ? "log.message" : eventId.Name,
                Message = formatter(state, exception),
                Exception = exception,
                CaptureState = () => ProviderStateCapture.Capture(state, eventId.Id),
                CaptureScopes = scopes is null ? null : depth => ProviderScopeCapture.Capture(scopes, depth)
            });
        }
    }
}
