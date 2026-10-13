using CIS.Logging.Events;
using CIS.Logging.Rendering;
using CIS.Logging.Runtime;
using CIS.Logging.Scopes;
using CIS.Text;

namespace CIS.Logging;

/// <summary>Owns logger context and independent output admission.</summary>
/// <remarks>Public creation and bounded shutdown are implemented in later roadmap tasks.</remarks>
public sealed class CoreLogRuntime
{
    private readonly object admissionGate;
    private readonly OutputQueues? queuedOutputs;
    private readonly ConsoleOutputWriter? consoleWriter;
    private readonly EventMetadata metadata;
    private readonly TimeProvider timeProvider;
    private readonly int maximumEntryBytes;
    private readonly Output[] outputs;
    private bool admissionClosed;
    private DateTimeOffset? shutdownStartedAtUtc;
    private long invalidEventCount;
    private long truncatedEventCount;

    // The direct internal admission seam remains available for existing focused qualification.
    internal CoreLogRuntime(EventMetadata metadata, LoggingOptions options, ILogOutputAdmission? console,
        ILogOutputAdmission? file, TimeProvider? timeProvider = null, object? sharedAdmissionGate = null, OutputQueues? queuedOutputs = null, ConsoleOutputWriter? consoleWriter = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);
        CoreLogLevel consoleLevel = options.EffectiveConsoleLevel;
        CoreLogLevel fileLevel = options.EffectiveFileLevel;
        bool consoleEnabled = consoleLevel != CoreLogLevel.None;
        bool fileEnabled = !string.IsNullOrWhiteSpace(options.FilePath) && fileLevel != CoreLogLevel.None;
        int maximumBytes = options.MaxEntryBytes;
        ValidateSettings(consoleLevel, fileLevel, maximumBytes);
        admissionGate = sharedAdmissionGate ?? new object();
        this.queuedOutputs = queuedOutputs;
        this.consoleWriter = consoleWriter;
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

    // Actual capture -> ticketed Channels -> sequential trusted writers; physical output ownership comes later.
    internal static CoreLogRuntime CreateQueued(EventMetadata metadata, LoggingOptions options,
        Func<AdmittedEvent, ValueTask>? consoleWrite, Func<AdmittedEvent, ValueTask>? fileWrite, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return ComposeQueued(metadata, CaptureQueueOptions(options), consoleWrite, fileWrite, timeProvider);
    }

    // Console settings are frozen before selecting the stream or starting any output worker.
    internal static CoreLogRuntime CreateConsoleQueued(EventMetadata metadata, LoggingOptions options,
        Func<AdmittedEvent, ValueTask>? fileWrite = null, TimeProvider? timeProvider = null,
        Func<ConsoleOutputStream, Stream>? acquireConsole = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        LoggingOptions selected = CaptureQueueOptions(options);
        ValidateSettings(selected.EffectiveConsoleLevel, selected.EffectiveFileLevel, selected.MaxEntryBytes);
        if (selected.QueueCapacity is < LoggingOptions.MinimumQueueCapacity or > LoggingOptions.MaximumQueueCapacity)
        { throw new ArgumentOutOfRangeException(nameof(options), "Use the configured queue-capacity range."); }
        if (!Enum.IsDefined(selected.ConsoleFormat) || !Enum.IsDefined(selected.HumanDetail) || !Enum.IsDefined(selected.ConsoleStream))
        { throw new ArgumentException("Use declared console presentation and stream values.", nameof(options)); }
        ConsoleOutputWriter? writer = null;
        if (selected.ConsoleEnabled)
        {
            Stream stream = acquireConsole is null ?
                selected.ConsoleStream == ConsoleOutputStream.StandardError ? Console.OpenStandardError() : Console.OpenStandardOutput() :
                acquireConsole(selected.ConsoleStream);
            if (stream is null) { throw new InvalidOperationException("Console selection must return a stream."); }
            writer = new ConsoleOutputWriter(stream, selected.ConsoleFormat, selected.HumanDetail, selected.MaxEntryBytes);
        }
        return ComposeQueued(metadata, selected, writer is null ? null : writer.WriteAsync, fileWrite, timeProvider, writer);
    }

    private static LoggingOptions CaptureQueueOptions(LoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new LoggingOptions
        {
            ConsoleLevel = options.EffectiveConsoleLevel,
            FileLevel = options.EffectiveFileLevel,
            FilePath = options.FilePath,
            MaxEntryBytes = options.MaxEntryBytes,
            QueueCapacity = options.QueueCapacity,
            ConsoleFormat = options.ConsoleFormat,
            ConsoleStream = options.ConsoleStream,
            HumanDetail = options.HumanDetail
        };
    }

    private static CoreLogRuntime ComposeQueued(EventMetadata metadata, LoggingOptions selected,
        Func<AdmittedEvent, ValueTask>? consoleWrite, Func<AdmittedEvent, ValueTask>? fileWrite,
        TimeProvider? timeProvider, ConsoleOutputWriter? writer = null)
    {
        ValidateSettings(selected.EffectiveConsoleLevel, selected.EffectiveFileLevel, selected.MaxEntryBytes);
        var gate = new object();
        var queues = new OutputQueues(selected.QueueCapacity, selected.ConsoleEnabled, selected.FileEnabled, consoleWrite, fileWrite, gate);
        try { return new CoreLogRuntime(metadata, selected, queues.Console, queues.File, timeProvider, gate, queues, writer); }
        catch { queues.Complete(); throw; }
    }
    internal OutputQueues? QueuedOutputs => queuedOutputs;
    internal RuntimeAccounting CaptureAccounting()
    {
        if (queuedOutputs is null) { throw new InvalidOperationException("Accounting requires actual queued output composition."); }
        lock (admissionGate)
        {
            // Output/disposition/truncation/closure changes share this gate. Invalid increments
            // independently, so its atomic read chooses the observation point while the rest is stable.
            return new RuntimeAccounting(admissionClosed, InvalidEventCount, TruncatedEventCount,
                queuedOutputs.Console?.CaptureAccounting(), queuedOutputs.File?.CaptureAccounting());
        }
    }

    /// <summary>Returns an owned immutable live observation without routing a log or performing output I/O.</summary>
    /// <remarks>Available after faults and admission closure. This does not register a fixed flush target.</remarks>
    /// <exception cref="InvalidOperationException">The runtime uses the internal admission-control seam without actual output queues.</exception>
    public CoreLogDiagnostics GetDiagnostics()
    {
        lock (admissionGate)
        {
            RuntimeAccounting counts = CaptureAccounting();
            var values = new List<LogOutputDiagnostics>(2);
            if (counts.Console is OutputAccounting console) { values.Add(new LogOutputDiagnostics("console", console)); }
            if (counts.File is OutputAccounting file) { values.Add(new LogOutputDiagnostics("file", file)); }
            CoreLogState state = !counts.AdmissionClosed ? CoreLogState.Running :
                values.All(value => value.State == LogOutputState.Stopped) ? CoreLogState.Stopped : CoreLogState.Stopping;
            return new CoreLogDiagnostics(state, counts.AdmissionClosed, DateTimeOffset.UtcNow, shutdownStartedAtUtc,
                counts.InvalidEventCount, counts.TruncatedEventCount, values);
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
        bool renderConsole;
        lock (admissionGate)
        {
            if (admissionClosed) { return Stopped(isTruncated: false); }
            if (!outputs.Any(output => Interested(output, level))) { return FinishWithoutCapture(level, LogWriteOutcome.Filtered); }
            if (!outputs.Any(output => Interested(output, level) && !output.Admission.IsFaulted))
            {
                return FinishWithoutCapture(level, LogWriteOutcome.Rejected);
            }
            renderConsole = consoleWriter is not null && outputs.Any(output => output.Name == "console" && Interested(output, level) && !output.Admission.IsFaulted);
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
        RenderedEvent? preparedConsole = null;
        // Other owned encoding/rendering consistency errors are Core defects, not invalid caller content.
        try
        {
            captured = EventEncoder.Encode(facts, maximumEntryBytes);
            if (renderConsole) { preparedConsole = consoleWriter!.Prepare(captured); }
        }
        catch (EventEnvelopeTooLargeException) { return Invalid("EnvelopeTooLarge"); }
        bool isTruncated = captured.IsTruncated || preparedConsole?.IsTruncated == true;
        lock (admissionGate)
        {
            if (isTruncated) { IncrementLossCounter(ref truncatedEventCount); }
            if (admissionClosed) { return Stopped(isTruncated); }
            return Admit(level, captured, preparedConsole?.Line, isTruncated);
        }
    }

    internal void CloseAdmission()
    {
        lock (admissionGate) { shutdownStartedAtUtc ??= DateTimeOffset.UtcNow; admissionClosed = true; queuedOutputs?.Complete(); }
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

    private LogWriteResult Admit(CoreLogLevel level, EncodedEvent captured, ReadOnlyMemory<byte>? consoleLine, bool isTruncated)
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
                else if (output.Name == "console" && consoleLine is { } preparedLine)
                { status = queuedOutputs!.Console!.TryAdmit(captured, preparedLine, out sequence); }
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
        return new LogWriteResult(outcome, observations, isTruncated: isTruncated);
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
    private static void ValidateSettings(CoreLogLevel console, CoreLogLevel file, int maximumBytes)
    {
        ValidateLevel(console); ValidateLevel(file);
        if (maximumBytes is < LoggingOptions.MinimumMaxEntryBytes or > LoggingOptions.MaximumMaxEntryBytes) { throw new ArgumentOutOfRangeException("options"); }
    }
    private static void ValidateLevel(CoreLogLevel level)
    {
        if (!Enum.IsDefined(level)) { throw new ArgumentOutOfRangeException(nameof(level), "Use a declared logging level."); }
    }
    private sealed record Output(string Name, CoreLogLevel Threshold, ILogOutputAdmission Admission);
}
