using CIS.Logging.Events;
using CIS.Logging.Rendering;
using CIS.Logging.Runtime;
using CIS.Logging.Scopes;
using CIS.Text;

namespace CIS.Logging;

/// <summary>Owns logger context, independent output admission and one shared bounded stop operation.</summary>
public sealed class CoreLogRuntime
{
    private readonly object admissionGate;
    private readonly OutputQueues? queuedOutputs;
    private readonly ConsoleOutputWriter? consoleWriter;
    private readonly FileOutputWriter? fileWriter;
    private readonly bool recoveredPartialSegment;
    private readonly EventMetadata metadata;
    private readonly TimeProvider timeProvider;
    private readonly int maximumEntryBytes;
    private readonly TimeSpan shutdownTimeout;
    private Task<LogStopResult>? stopTask;
    private LogStopResult? lastStopResult;
    private readonly Output[] outputs;
    private bool admissionClosed;
    private DateTimeOffset? shutdownStartedAtUtc;
    private long invalidEventCount;
    private long truncatedEventCount;

    // The direct internal admission seam remains available for existing focused qualification.
    internal CoreLogRuntime(EventMetadata metadata, LoggingOptions options, ILogOutputAdmission? console,
        ILogOutputAdmission? file, TimeProvider? timeProvider = null, object? sharedAdmissionGate = null, OutputQueues? queuedOutputs = null, ConsoleOutputWriter? consoleWriter = null, FileOutputWriter? fileWriter = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);
        CoreLogLevel consoleLevel = options.EffectiveConsoleLevel;
        CoreLogLevel fileLevel = options.EffectiveFileLevel;
        bool consoleEnabled = consoleLevel != CoreLogLevel.None;
        bool fileEnabled = !string.IsNullOrWhiteSpace(options.FilePath) && fileLevel != CoreLogLevel.None;
        int maximumBytes = options.MaxEntryBytes;
        ValidateSettings(consoleLevel, fileLevel, maximumBytes);
        ValidateShutdownTimeout(options.ShutdownTimeoutMilliseconds);
        admissionGate = sharedAdmissionGate ?? new object();
        this.queuedOutputs = queuedOutputs;
        this.consoleWriter = consoleWriter;
        this.fileWriter = fileWriter;
        recoveredPartialSegment = fileWriter?.RecoveredPartialSegment == true;
        this.metadata = metadata;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        maximumEntryBytes = maximumBytes;
        shutdownTimeout = options.ShutdownTimeout;
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
        Func<AdmittedEvent, ValueTask>? consoleWrite, Func<AdmittedEvent, ValueTask>? fileWrite, TimeProvider? timeProvider = null,
        Func<ValueTask>? consoleFlush = null, Func<ValueTask>? fileFlush = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return ComposeQueued(metadata, CaptureQueueOptions(options), consoleWrite, fileWrite, timeProvider, consoleFlush: consoleFlush, fileFlush: fileFlush);
    }

    // Console settings are frozen before selecting the stream or starting any output worker.
    internal static CoreLogRuntime CreateConsoleQueued(EventMetadata metadata, LoggingOptions options,
        Func<AdmittedEvent, ValueTask>? fileWrite = null, TimeProvider? timeProvider = null,
        Func<ConsoleOutputStream, Stream>? acquireConsole = null, Func<Func<Task>, Task>? launchWorker = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        LoggingOptions selected = CaptureQueueOptions(options);
        ValidateSettings(selected.EffectiveConsoleLevel, selected.EffectiveFileLevel, selected.MaxEntryBytes);
        if (selected.QueueCapacity is < LoggingOptions.MinimumQueueCapacity or > LoggingOptions.MaximumQueueCapacity)
        { throw new ArgumentOutOfRangeException(nameof(options), "Use the configured queue-capacity range."); }
        if (!Enum.IsDefined(selected.ConsoleFormat) || !Enum.IsDefined(selected.HumanDetail) || !Enum.IsDefined(selected.ConsoleStream))
        { throw new ArgumentException("Use declared console presentation and stream values.", nameof(options)); }
        ConsoleOutputWriter? writer = AcquireConsoleWriter(selected, acquireConsole);
        return ComposeQueued(metadata, selected, writer is null ? null : writer.WriteAsync, fileWrite, timeProvider, writer, launchWorker);
    }

    internal static CoreLogRuntime CreateConfiguredQueued(EventMetadata metadata, LoggingOptions options, TimeProvider? timeProvider = null,
        Func<ConsoleOutputStream, Stream>? acquireConsole = null, Func<Func<Task>, Task>? launchWorker = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        LoggingOptions selected = CaptureQueueOptions(options);
        ValidateSettings(selected.EffectiveConsoleLevel, selected.EffectiveFileLevel, selected.MaxEntryBytes);
        if (selected.QueueCapacity is < LoggingOptions.MinimumQueueCapacity or > LoggingOptions.MaximumQueueCapacity
            || selected.FileMaxBytes is < LoggingOptions.MinimumFileMaxBytes or > LoggingOptions.MaximumFileMaxBytes
            || selected.FileRetainedFiles is < LoggingOptions.MinimumFileRetainedFiles or > LoggingOptions.MaximumFileRetainedFiles
            || selected.FileEnabled && (selected.FileMaxBytes < selected.MaxEntryBytes
                || selected.FlushIntervalMilliseconds is < LoggingOptions.MinimumFlushIntervalMilliseconds or > LoggingOptions.MaximumFlushIntervalMilliseconds))
        { throw new ArgumentOutOfRangeException(nameof(options)); }
        if (!Enum.IsDefined(selected.ConsoleFormat) || !Enum.IsDefined(selected.ConsoleStream) || !Enum.IsDefined(selected.FileFormat) || !Enum.IsDefined(selected.HumanDetail))
        { throw new ArgumentException("Use declared output presentation values.", nameof(options)); }
        if (selected.FileEnabled && !Path.IsPathFullyQualified(selected.FilePath!))
        { throw new ArgumentException("File output requires an already captured absolute path.", nameof(options)); }
        ConsoleOutputWriter? console = AcquireConsoleWriter(selected, acquireConsole);
        FileOutputWriter? file = selected.FileEnabled ? new(selected.FilePath!, selected.FileFormat, selected.HumanDetail, selected.MaxEntryBytes, selected.FileMaxBytes, selected.FileRetainedFiles) : null;
        return ComposeQueued(metadata, selected, console is null ? null : console.WriteAsync, file is null ? null : file.WriteAsync, timeProvider, console, launchWorker, file);
    }

    private static ConsoleOutputWriter? AcquireConsoleWriter(LoggingOptions selected, Func<ConsoleOutputStream, Stream>? acquireConsole)
    {
        ConsoleOutputWriter? writer = null;
        if (selected.ConsoleEnabled)
        {
            try
            {
                Stream stream = acquireConsole is null ?
                    selected.ConsoleStream == ConsoleOutputStream.StandardError ? Console.OpenStandardError() : Console.OpenStandardOutput() :
                    acquireConsole(selected.ConsoleStream);
                if (stream is null) { throw new CoreLogInitializationException("console", "ConsoleInitializationFailed"); }
                writer = new ConsoleOutputWriter(stream, selected.ConsoleFormat, selected.HumanDetail, selected.MaxEntryBytes);
            }
            catch (CoreLogInitializationException) { throw; }
            catch { throw new CoreLogInitializationException("console", "ConsoleInitializationFailed"); }
        }
        return writer;
    }

    private static LoggingOptions CaptureQueueOptions(LoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateShutdownTimeout(options.ShutdownTimeoutMilliseconds);
        return new LoggingOptions
        {
            ConsoleLevel = options.EffectiveConsoleLevel,
            FileLevel = options.EffectiveFileLevel,
            FilePath = options.FilePath,
            MaxEntryBytes = options.MaxEntryBytes,
            QueueCapacity = options.QueueCapacity,
            ConsoleFormat = options.ConsoleFormat,
            ConsoleStream = options.ConsoleStream,
            HumanDetail = options.HumanDetail,
            FileFormat = options.FileFormat,
            FileMaxBytes = options.FileMaxBytes,
            FileRetainedFiles = options.FileRetainedFiles,
            FlushIntervalMilliseconds = options.FlushIntervalMilliseconds,
            ShutdownTimeoutMilliseconds = options.ShutdownTimeoutMilliseconds
        };
    }

    private static CoreLogRuntime ComposeQueued(EventMetadata metadata, LoggingOptions selected,
        Func<AdmittedEvent, ValueTask>? consoleWrite, Func<AdmittedEvent, ValueTask>? fileWrite,
        TimeProvider? timeProvider, ConsoleOutputWriter? writer = null, Func<Func<Task>, Task>? launchWorker = null, FileOutputWriter? fileWriter = null, Func<ValueTask>? consoleFlush = null, Func<ValueTask>? fileFlush = null)
    {
        ValidateSettings(selected.EffectiveConsoleLevel, selected.EffectiveFileLevel, selected.MaxEntryBytes);
        var gate = new object();
        var queues = new OutputQueues(selected.QueueCapacity, selected.ConsoleEnabled, selected.FileEnabled, consoleWrite, fileWrite, gate, launchWorker, fileWriter,
            writer is null ? consoleFlush : writer.FlushAsync, fileWriter is null ? fileFlush : fileWriter.FlushAsync,
            writer is null ? null : writer.WriteAsync, fileWriter is null ? null : fileWriter.WriteAsync,
            writer is null ? null : writer.FlushAsync, fileWriter is null ? null : fileWriter.FlushAsync);
        try
        {
            writer?.Initialize();
            fileWriter?.Initialize();
            var runtime = new CoreLogRuntime(metadata, selected, queues.Console, queues.File, timeProvider, gate, queues, writer, fileWriter);
            if (fileWriter is not null)
            {
                try { queues.File!.StartFileFlushTimer(runtime.timeProvider, selected.FlushInterval); }
                catch { throw new CoreLogInitializationException("file", "FileFlushTimerInitializationFailed"); }
            }
            return runtime;
        }
        catch { queues.Complete(); queues.Completion.GetAwaiter().GetResult(); fileWriter?.Dispose(); throw; }
    }
    internal OutputQueues? QueuedOutputs => queuedOutputs;
    private static void ValidateShutdownTimeout(int milliseconds)
    {
        if (milliseconds is < LoggingOptions.MinimumShutdownTimeoutMilliseconds or > LoggingOptions.MaximumShutdownTimeoutMilliseconds)
        { throw new ArgumentOutOfRangeException(nameof(milliseconds), "Use the configured shutdown-timeout range."); }
    }

    /// <summary>Starts or joins one shared bounded stop operation through its original accepted boundaries.</summary>
    /// <param name="cancellationToken">Cancels only this caller's wait; even an already-cancelled first caller starts shutdown.</param>
    /// <returns>The same owned immutable observation retained for all joining callers.</returns>
    /// <exception cref="OperationCanceledException">The caller cancels its own wait.</exception>
    /// <exception cref="InvalidOperationException">The runtime uses a trusted admission-test seam without actual output queues.</exception>
    /// <remarks>The configured overall timeout is frozen at creation. UTC is an observation label; elapsed time is monotonic. At expiry, unstarted accepted records are abandoned. Cancellation of active writes/flushes is requested but is not guaranteed, and no new flush, retry or replacement writer starts. Completed means terminal dispositions, not successful delivery or resource release. A timed-out observation can retain pending I/O; its later completion does not change the cached result.</remarks>
    public Task<LogStopResult> StopAsync(CancellationToken cancellationToken)
    {
        if (queuedOutputs is null) { throw new InvalidOperationException("Stop requires actual queued output composition."); }
        Task<LogStopResult>? shared;
        lock (admissionGate) { shared = stopTask; }
        if (shared is null)
        {
            // Caller clock code is never invoked under admission synchronization.
            // Preliminary concurrent samples are harmless; only the winning caller owns the deadline.
            long started = timeProvider.GetTimestamp();
            RuntimeFlushBoundary boundary = default;
            TaskCompletionSource<LogStopResult>? completion = null;
            lock (admissionGate)
            {
                if (stopTask is null)
                {
                    completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    stopTask = completion.Task;
                    // Register final controls before a closing idle worker is allowed to exit.
                    boundary = RegisterFlushBoundary();
                    shutdownStartedAtUtc ??= boundary.BoundaryCapturedAtUtc;
                    admissionClosed = true;
                    queuedOutputs.Complete();
                }
                shared = stopTask;
            }
            if (completion is not null)
            {
                // Start deadline acquisition directly, without a delayed ThreadPool launch.
                // Only continuations/timers outlive this call; they do not retain the caller's flow.
                if (ExecutionContext.IsFlowSuppressed()) { _ = RunStopAsync(boundary, started, completion); }
                else { using (ExecutionContext.SuppressFlow()) { _ = RunStopAsync(boundary, started, completion); } }
            }
        }
        // WaitAsync can prefer an already-completed task; an already-cancelled caller must still cancel.
        return cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<LogStopResult>(cancellationToken) : shared!.WaitAsync(cancellationToken);
    }

    private async Task RunStopAsync(RuntimeFlushBoundary boundary, long started, TaskCompletionSource<LogStopResult> completion)
    {
        try
        {
            if (boundary.Console is null && boundary.File is null)
            { PublishStop(CaptureFlushResult(boundary), completion); return; }
            await using var deadline = new FlushDeadline(timeProvider, started, shutdownTimeout, () => CaptureFlushResult(boundary), () => CaptureStopDeadline(boundary));
            try
            {
                await boundary.WaitForWriterFlushAsync(deadline.Token).ConfigureAwait(false);
                if (boundary.Console is { } console)
                { await queuedOutputs!.Console!.WaitForCoveredRecordsAsync("console", console, deadline.Token).ConfigureAwait(false); }
                if (boundary.File is { } file)
                { await queuedOutputs!.File!.WaitForCoveredRecordsAsync("file", file, deadline.Token).ConfigureAwait(false); }
                try { await queuedOutputs!.Completion.WaitAsync(deadline.Token).ConfigureAwait(false); }
                catch (Exception) when ((queuedOutputs!.Completion.IsFaulted || queuedOutputs.Completion.IsCanceled) && (queuedOutputs.Console?.IsFaulted == true || queuedOutputs.File?.IsFaulted == true))
                { /* Terminal writer faults remain safe output results, not business-call I/O exceptions. */ }
            }
            catch (OperationCanceledException) when (deadline.Token.IsCancellationRequested) { }
            // Publish the frozen result before asynchronous deadline-timer cleanup can wait on a callback join.
            PublishStop(deadline.ObserveCompletion(), completion);
        }
        catch (Exception error) { completion.TrySetException(error); }
    }

    private LogFlushResult CaptureStopDeadline(RuntimeFlushBoundary boundary)
    {
        LogFlushResult observation;
        lock (admissionGate)
        {
            queuedOutputs!.AbandonUnstarted();
            observation = CaptureFlushResult(boundary);
        }
        // Freeze actual pending work before cooperative cancellation can settle it.
        queuedOutputs.RequestIoCancellation();
        return observation;
    }
    private void PublishStop(LogFlushResult observation, TaskCompletionSource<LogStopResult> completion)
    {
        var result = new LogStopResult(observation);
        lock (admissionGate) { lastStopResult = result; }
        completion.TrySetResult(result);
    }


    /// <summary>Waits through fixed accepted boundaries and confirms each enabled writer's buffer flush.</summary>
    /// <param name="timeout">One positive finite overall wait budget; every positive TimeSpan is supported.</param>
    /// <param name="cancellationToken">Cancels only this caller's wait, preserving shared worker requests.</param>
    /// <returns>An owned immutable observation, including unresolved work at the deadline.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The timeout is zero, negative or infinite.</exception>
    /// <exception cref="OperationCanceledException">The caller token is cancelled, including after boundary registration.</exception>
    /// <exception cref="InvalidOperationException">The runtime uses an internal admission-control seam without actual output queues.</exception>
    /// <remarks>UTC labels are system observations; elapsed deadlines use the runtime TimeProvider. Flush does not stop admission or guarantee disk durability.</remarks>
    public Task<LogFlushResult> FlushAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout.Ticks <= 0) { throw new ArgumentOutOfRangeException(nameof(timeout)); }
        long started = timeProvider.GetTimestamp();
        RuntimeFlushBoundary boundary = RegisterFlushBoundary();
        return ObserveFlushAsync(boundary, started, timeout, cancellationToken);
    }
    private async Task<LogFlushResult> ObserveFlushAsync(RuntimeFlushBoundary boundary, long started, TimeSpan timeout, CancellationToken caller)
    {
        caller.ThrowIfCancellationRequested();
        if (boundary.Console is null && boundary.File is null) { return CaptureFlushResult(boundary); }
        await using var deadline = new FlushDeadline(timeProvider, started, timeout, () => CaptureFlushResult(boundary));
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(caller, deadline.Token);
        try
        {
            await boundary.WaitForWriterFlushAsync(wait.Token).ConfigureAwait(false);
            // A terminal unavailable flush can precede the disposition of an already-started write.
            if (boundary.Console is { } console)
            { await queuedOutputs!.Console!.WaitForCoveredRecordsAsync("console", console, wait.Token).ConfigureAwait(false); }
            if (boundary.File is { } file)
            { await queuedOutputs!.File!.WaitForCoveredRecordsAsync("file", file, wait.Token).ConfigureAwait(false); }
        }
        catch (OperationCanceledException) when (wait.IsCancellationRequested)
        { caller.ThrowIfCancellationRequested(); }
        caller.ThrowIfCancellationRequested();
        return deadline.ObserveCompletion();
    }
    private LogFlushResult CaptureFlushResult(RuntimeFlushBoundary boundary)
    {
        lock (admissionGate)
        {
            var observations = new List<LogOutputDrainResult>(2);
            if (boundary.Console is { } console) { observations.Add(queuedOutputs!.Console!.CaptureDrain("console", console)); }
            if (boundary.File is { } file) { observations.Add(queuedOutputs!.File!.CaptureDrain("file", file)); }
            return new LogFlushResult(boundary.BoundaryCapturedAtUtc, DateTimeOffset.UtcNow,
                boundary.InvalidEventCount, boundary.TruncatedEventCount, observations);
        }
    }
    internal RuntimeFlushBoundary RegisterFlushBoundary()
    {
        if (queuedOutputs is null) { throw new InvalidOperationException("Flush boundaries require actual queued output composition."); }
        lock (admissionGate)
        {
            // Pure system UTC sampling here does not invoke caller factories, payloads or the event clock.
            DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
            OutputFlushBoundary? console = queuedOutputs.Console?.RegisterFlushBoundary();
            OutputFlushBoundary? file = queuedOutputs.File?.RegisterFlushBoundary();
            return new RuntimeFlushBoundary(capturedAt, InvalidEventCount, TruncatedEventCount, console, file);
        }
    }
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
            if (counts.File is OutputAccounting file) { values.Add(new LogOutputDiagnostics("file", file, recoveredPartialSegment)); }
            CoreLogState state = !counts.AdmissionClosed ? CoreLogState.Running :
                values.All(value => value.State == LogOutputState.Stopped) ? CoreLogState.Stopped : CoreLogState.Stopping;
            return new CoreLogDiagnostics(state, counts.AdmissionClosed, DateTimeOffset.UtcNow, shutdownStartedAtUtc,
                counts.InvalidEventCount, counts.TruncatedEventCount, values, lastStopResult);
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
        bool renderConsole, renderFile;
        lock (admissionGate)
        {
            if (admissionClosed) { return Stopped(isTruncated: false); }
            if (!outputs.Any(output => Interested(output, level))) { return FinishWithoutCapture(level, LogWriteOutcome.Filtered); }
            if (!outputs.Any(output => Interested(output, level) && !output.Admission.IsFaulted))
            {
                return FinishWithoutCapture(level, LogWriteOutcome.Rejected);
            }
            renderConsole = consoleWriter is not null && outputs.Any(output => output.Name == "console" && Interested(output, level) && !output.Admission.IsFaulted);
            renderFile = fileWriter is not null && outputs.Any(output => output.Name == "file" && Interested(output, level) && !output.Admission.IsFaulted);
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
        RenderedEvent? preparedConsole = null, preparedFile = null;
        // Other owned encoding/rendering consistency errors are Core defects, not invalid caller content.
        try
        {
            captured = EventEncoder.Encode(facts, maximumEntryBytes);
            if (renderConsole) { preparedConsole = consoleWriter!.Prepare(captured); }
            if (renderFile) { preparedFile = fileWriter!.Prepare(captured); }
        }
        catch (EventEnvelopeTooLargeException) { return Invalid("EnvelopeTooLarge"); }
        bool isTruncated = captured.IsTruncated || preparedConsole?.IsTruncated == true || preparedFile?.IsTruncated == true;
        lock (admissionGate)
        {
            if (isTruncated) { IncrementLossCounter(ref truncatedEventCount); }
            if (admissionClosed) { return Stopped(isTruncated); }
            return Admit(level, captured, preparedConsole?.Line, preparedFile?.Line, isTruncated);
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

    private LogWriteResult Admit(CoreLogLevel level, EncodedEvent captured, ReadOnlyMemory<byte>? consoleLine, ReadOnlyMemory<byte>? fileLine, bool isTruncated)
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
                else if (output.Name == "file" && fileLine is { } preparedFileLine)
                { status = queuedOutputs!.File!.TryAdmit(captured, preparedFileLine, out sequence); }
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
