using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;

namespace CIS.CommonTesting;

/// <summary>One captured log call; structured values and exception identity are shallow caller-owned references.</summary>
/// <param name="Level">The supplied level.</param>
/// <param name="EventId">The stable event identity.</param>
/// <param name="Message">The formatter output captured during the call.</param>
/// <param name="Fields">A copied, read-only structured field map.</param>
/// <param name="Exception">The original supplied exception.</param>
public sealed record RecordedLog(LogLevel Level, EventId EventId, string Message,
    IReadOnlyDictionary<string, object?> Fields, Exception? Exception);

/// <summary>Records a bounded event tail without a logging pipeline, ambient scopes or test-framework dependency.</summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly BoundedRecords<RecordedLog> entries;
    private int minimumLevel;

    /// <summary>Chooses the retained event capacity and enabled minimum level.</summary>
    public RecordingLogger(int capacity = 256, LogLevel minimumLevel = LogLevel.Trace)
    {
        entries = new(capacity);
        MinimumLevel = minimumLevel;
    }
    /// <summary>Gets or sets the enabled level; None disables all calls.</summary>
    public LogLevel MinimumLevel
    {
        get => (LogLevel)Volatile.Read(ref minimumLevel);
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            Volatile.Write(ref minimumLevel, (int)value);
        }
    }
    /// <summary>Gets a detached bounded event snapshot.</summary>
    public IReadOnlyList<RecordedLog> Entries => entries.Snapshot();
    /// <summary>Gets the saturating count of enabled calls since Clear, including evicted events.</summary>
    public long TotalCount => entries.Total;
    /// <summary>Gets the number of evicted events since Clear.</summary>
    public long DroppedCount => entries.Dropped;
    /// <summary>Clears retained events and counters. Earlier returned snapshots remain caller-owned.</summary>
    public void Clear() => entries.Clear();
    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= MinimumLevel;
    /// <inheritdoc />
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => EmptyScope.Instance;
    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }
        ArgumentNullException.ThrowIfNull(formatter);
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (state is IEnumerable<KeyValuePair<string, object?>> structured)
        {
            foreach (var pair in structured)
            {
                fields[pair.Key] = pair.Value;
            }
        }
        entries.Add(new(logLevel, eventId, formatter(state, exception),
            new ReadOnlyDictionary<string, object?>(fields), exception));
    }

    private sealed class EmptyScope : IDisposable
    {
        internal static readonly EmptyScope Instance = new();
        public void Dispose()
        {
        }
    }
}
