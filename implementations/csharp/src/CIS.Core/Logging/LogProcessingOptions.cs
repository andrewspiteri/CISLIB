namespace CIS.Logging;

/// <summary>Application callbacks captured once at runtime startup and run by the incoming worker.</summary>
/// <remarks>Do not mutate setup concurrently with creation. Callbacks run serially outside admission locks.
/// Interceptors run in registration order before filtering, routing and duplicate checking. Exceptions fail
/// the event closed. Logging from a callback flow is rejected; flush and stop throw InvalidOperationException.</remarks>
public sealed class LogProcessingOptions
{
    /// <summary>Ordered payload transformations, including application redaction. Null results are failures.</summary>
    public IReadOnlyList<Func<LogProcessingEvent, LogEventChanges>> Interceptors { get; set; } = [];
    /// <summary>Optional global inclusion predicate applied after every interceptor.</summary>
    public Func<LogProcessingEvent, bool>? Filter { get; set; }
    /// <summary>Optional inclusion predicate per destination, after its severity threshold. Cannot widen that threshold.</summary>
    public Func<string, LogProcessingEvent, bool>? Route { get; set; }
    /// <summary>Optional application-defined duplicate policy; null disables suppression.</summary>
    public LogDuplicateSuppression? DuplicateSuppression { get; set; }
}

/// <summary>Opt-in duplicate identity and a bounded, non-sliding monotonic time window.</summary>
public sealed class LogDuplicateSuppression
{
    /// <summary>Creates an ordinal key policy. Null keys bypass suppression; keys have at most 1024 UTF-16 units.</summary>
    /// <remarks>The application includes all distinguishing occurrence facts in its key. Entries expire from
    /// the last event admitted to at least one writer. Capacity evicts the oldest entry without suppressing
    /// the new event. This provides no delivery or retry guarantee.</remarks>
    public LogDuplicateSuppression(Func<LogProcessingEvent, string?> key, TimeSpan window, int capacity = 1024)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (window <= TimeSpan.Zero) { throw new ArgumentOutOfRangeException(nameof(window)); }
        if (capacity is < 1 or > 65536) { throw new ArgumentOutOfRangeException(nameof(capacity)); }
        Key = key; Window = window; Capacity = capacity;
    }
    /// <summary>The application-supplied occurrence identity, evaluated after interceptors and routing.</summary>
    public Func<LogProcessingEvent, string?> Key { get; }
    /// <summary>The interval since the last nonsuppressed occurrence admitted to a writer.</summary>
    public TimeSpan Window { get; }
    /// <summary>The maximum retained keys; oldest entries are evicted at this bound.</summary>
    public int Capacity { get; }
}

/// <summary>Caller-owned interceptor changes, copied immediately after the callback returns.</summary>
/// <remarks>Null properties preserve their captured value. Empty attributes replace the complete attribute
/// map, including scope attributes; the Microsoft provider's integer event_id remains protected.
/// Identity, severity, occurrence time and correlation cannot be changed.
/// Do not mutate these values concurrently with capture.</remarks>
public sealed class LogEventChanges
{
    /// <summary>Optional replacement message, subject to normal capture and entry bounds.</summary>
    public string? Message { get; set; }
    /// <summary>Optional replacement template; ClearMessageTemplate removes it instead.</summary>
    public string? MessageTemplate { get; set; }
    /// <summary>Whether to remove the template, taking precedence over a replacement.</summary>
    public bool ClearMessageTemplate { get; set; }
    /// <summary>Optional complete replacement of the copied typed attribute map.</summary>
    public IReadOnlyDictionary<string, LogValue>? Attributes { get; set; }
    /// <summary>Whether to remove the complete captured exception tree.</summary>
    public bool RemoveException { get; set; }
}
