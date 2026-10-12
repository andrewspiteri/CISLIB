using CIS.Logging.Events;
using CIS.Logging.Runtime;
using CIS.Logging.Scopes;
using CIS.Text;

namespace CIS.Logging;

/// <summary>Owns logger context and independent output admission.</summary>
/// <remarks>Output composition and public creation/shutdown are implemented in later roadmap tasks.</remarks>
public sealed class CoreLogRuntime
{
    private readonly object admissionGate = new();
    private readonly EventMetadata metadata;
    private readonly TimeProvider timeProvider;
    private readonly int maximumEntryBytes;
    private readonly Output[] outputs;
    private bool admissionClosed;
    private long invalidEventCount;
    private long truncatedEventCount;

    // Real logger/capture implementation, with internal output composition until workers exist.
    internal CoreLogRuntime(EventMetadata metadata, LoggingOptions options, ILogOutputAdmission? console,
        ILogOutputAdmission? file, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);
        CoreLogLevel consoleLevel = options.EffectiveConsoleLevel;
        CoreLogLevel fileLevel = options.EffectiveFileLevel;
        bool consoleEnabled = consoleLevel != CoreLogLevel.None;
        bool fileEnabled = !string.IsNullOrWhiteSpace(options.FilePath) && fileLevel != CoreLogLevel.None;
        int maximumBytes = options.MaxEntryBytes;
        ValidateLevel(consoleLevel);
        ValidateLevel(fileLevel);
        if (maximumBytes is < LoggingOptions.MinimumMaxEntryBytes or > LoggingOptions.MaximumMaxEntryBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
        this.metadata = metadata;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        maximumEntryBytes = maximumBytes;
        var configured = new List<Output>(2);
        Add("console", consoleEnabled, consoleLevel, console);
        Add("file", fileEnabled, fileLevel, file);
        outputs = configured.ToArray();

        void Add(string name, bool enabled, CoreLogLevel threshold, ILogOutputAdmission? admission)
        {
            if (enabled != (admission is not null))
            {
                throw new ArgumentException("Output composition must match enabled settings.", name);
            }
            if (admission is not null) { configured.Add(new Output(name, threshold, admission)); }
        }
    }

    internal OperationScopeStore Scopes { get; } = new();
    internal long InvalidEventCount => Interlocked.Read(ref invalidEventCount);
    internal long TruncatedEventCount => Interlocked.Read(ref truncatedEventCount);

    /// <summary>Creates a logger borrowing this runtime, including after admission closes.</summary>
    /// <exception cref="ArgumentNullException">Component is null.</exception>
    /// <exception cref="ArgumentException">Component is blank, malformed, contains controls or exceeds 255 scalars.</exception>
    public CoreLogger CreateLogger(string component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (string.IsNullOrWhiteSpace(component) || !UnicodeScalarText.IsValid(component, 255, allowControls: false))
        {
            throw new ArgumentException("Use a component of 1-255 valid Unicode scalars without controls.", nameof(component));
        }
        return new CoreLogger(this, component);
    }

    internal bool IsEnabled(CoreLogLevel level)
    {
        ValidateLevel(level);
        lock (admissionGate)
        {
            return !admissionClosed && outputs.Any(output => Interested(output, level) && !output.Admission.IsFaulted);
        }
    }

    internal LogWriteResult Write(string component, CoreLogLevel level, LogEventData? data, Func<LogEventData>? factory)
    {
        ValidateLevel(level);
        lock (admissionGate)
        {
            if (admissionClosed) { return Stopped(isTruncated: false); }
            if (!outputs.Any(output => Interested(output, level))) { return FinishWithoutCapture(level, LogWriteOutcome.Filtered); }
            if (!outputs.Any(output => Interested(output, level) && !output.Admission.IsFaulted))
            {
                return FinishWithoutCapture(level, LogWriteOutcome.Rejected);
            }
        }
        // No gate is held around caller factories, clock, scopes or payload callbacks.
        if (factory is not null) { data = factory(); }
        if (data is null) { return Invalid("InvalidFactoryResult"); }
        string? eventName = data.EventName;
        string? message = data.Message;
        string? template = data.MessageTemplate;
        string? operationId = data.OperationId;
        CapturedEventFacts facts;
        try
        {
            facts = EventProjection.Capture(metadata, Scopes.Capture(), component, level, data, timeProvider)!;
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
        {
            return Invalid(CaptureError(eventName, message, template, operationId));
        }
        EncodedEvent captured;
        try { captured = EventEncoder.Encode(facts, maximumEntryBytes); }
        catch (EventEnvelopeTooLargeException) { return Invalid("EnvelopeTooLarge"); }
        // Owned encoding consistency errors are Core defects, not invalid caller content.
        lock (admissionGate)
        {
            if (captured.IsTruncated) { IncrementLossCounter(ref truncatedEventCount); }
            if (admissionClosed) { return Stopped(captured.IsTruncated); }
            return Admit(level, captured);
        }
    }

    internal void CloseAdmission()
    {
        lock (admissionGate) { admissionClosed = true; }
    }

    private LogWriteResult Invalid(string code)
    {
        IncrementLossCounter(ref invalidEventCount);
        return new LogWriteResult(LogWriteOutcome.InvalidEvent, [], code);
    }

    // Loss counters retain a nonnegative lower bound once Int64 can no longer represent the total.
    private static void IncrementLossCounter(ref long counter)
    {
        long current = Interlocked.Read(ref counter);
        while (current != long.MaxValue)
        {
            long observed = Interlocked.CompareExchange(ref counter, current + 1, current);
            if (observed == current) { return; }
            current = observed;
        }
    }

    private LogWriteResult Stopped(bool isTruncated) => new(LogWriteOutcome.Stopped,
        outputs.Select(output => new LogOutputWriteResult(output.Name, LogOutputWriteStatus.Stopped, null)), isTruncated: isTruncated);

    private LogWriteResult FinishWithoutCapture(CoreLogLevel level, LogWriteOutcome outcome)
    {
        var observations = new List<LogOutputWriteResult>(outputs.Length);
        foreach (Output output in outputs)
        {
            LogOutputWriteStatus status;
            if (Interested(output, level))
            {
                output.Admission.RecordUnavailable();
                status = LogOutputWriteStatus.Faulted;
            }
            else
            {
                output.Admission.RecordFiltered();
                status = LogOutputWriteStatus.Filtered;
            }
            observations.Add(new LogOutputWriteResult(output.Name, status, null));
        }
        return new LogWriteResult(outcome, observations);
    }

    private LogWriteResult Admit(CoreLogLevel level, EncodedEvent captured)
    {
        var observations = new List<LogOutputWriteResult>(outputs.Length);
        int accepted = 0;
        int interested = 0;
        foreach (Output output in outputs)
        {
            long? sequence = null;
            LogOutputWriteStatus status;
            if (!Interested(output, level))
            {
                output.Admission.RecordFiltered();
                status = LogOutputWriteStatus.Filtered;
            }
            else
            {
                interested++;
                if (output.Admission.IsFaulted)
                {
                    output.Admission.RecordUnavailable();
                    status = LogOutputWriteStatus.Faulted;
                }
                else { status = output.Admission.TryAdmit(captured, out sequence); }
                if (status == LogOutputWriteStatus.Accepted) { accepted++; }
            }
            if (Interested(output, level) && status is not (LogOutputWriteStatus.Accepted or LogOutputWriteStatus.QueueFull or LogOutputWriteStatus.Faulted))
            {
                throw new InvalidOperationException("Output admission returned an invalid status.");
            }
            if (status == LogOutputWriteStatus.Accepted ? sequence is null or <= 0 : sequence is not null)
            {
                throw new InvalidOperationException("Output admission returned an invalid ticket.");
            }
            observations.Add(new LogOutputWriteResult(output.Name, status, sequence));
        }
        LogWriteOutcome outcome = LogWriteOutcome.Rejected;
        if (accepted == interested) { outcome = LogWriteOutcome.Accepted; }
        else if (accepted > 0) { outcome = LogWriteOutcome.PartiallyAccepted; }
        return new LogWriteResult(outcome, observations, isTruncated: captured.IsTruncated);
    }

    private static string CaptureError(string? name, string? message, string? template, string? operationId)
    {
        foreach (string? text in new[] { name, message, template, operationId })
        {
            if (text is not null && !UnicodeScalarText.IsValid(text, int.MaxValue, allowControls: true)) { return "InvalidUnicode"; }
        }
        if (string.IsNullOrEmpty(name) || !UnicodeScalarText.IsValid(name, 255, allowControls: false)) { return "InvalidEventName"; }
        if (message is null) { return "InvalidMessage"; }
        string? operation = operationId?.Trim();
        if (!string.IsNullOrEmpty(operation) && !UnicodeScalarText.IsValid(operation, 255, allowControls: false)) { return "InvalidOperationId"; }
        // Arbitrary cooperative callback/unsupported payload failures expose only this fixed code.
        return "UnsupportedValue";
    }

    private static bool Interested(Output output, CoreLogLevel level) => level != CoreLogLevel.None && level >= output.Threshold;
    private static void ValidateLevel(CoreLogLevel level)
    {
        if (!Enum.IsDefined(level)) { throw new ArgumentOutOfRangeException(nameof(level), "Use a declared logging level."); }
    }
    private sealed record Output(string Name, CoreLogLevel Threshold, ILogOutputAdmission Admission);
}
